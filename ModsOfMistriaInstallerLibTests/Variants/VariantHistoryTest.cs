using Garethp.ModsOfMistriaInstallerLib.Variants;

namespace ModsOfMistriaInstallerLibTests.Variants;

// The history is the promise that every variant id a save may hold keeps loading. Losing an
// entry means a player's placed furniture (and its chest contents) silently disappears.
[TestFixture]
public class VariantHistoryTest
{
    private string _mods = "";

    [SetUp]
    public void CreateModsFolder()
    {
        _mods = Path.Combine(Path.GetTempPath(), "aim_history_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_mods);
    }

    [TearDown]
    public void RemoveModsFolder()
    {
        if (Directory.Exists(_mods)) Directory.Delete(_mods, true);
    }

    private static ItemVariant Item(string look, string item = "test_lamp", string obj = "test_lamp") =>
        new("furniture:" + obj, look, item, obj, $"{item}_{look}", $"{obj}_{look}");

    private static AnimalVariant Animal(string look, string kind = "cow", string variant = "brown") =>
        new("animal:" + kind, look, kind, variant, $"{variant}_{look}");

    [Test]
    public void ShouldRoundTripThroughTheModsFolder()
    {
        var history = new VariantHistory();
        history.Remember([Item("lkone"), Item("lktwo")], [Animal("lkone"), Animal("lkone", kind: "chicken", variant: "brown")]);
        history.Save(_mods);

        var loaded = VariantHistory.Load(_mods);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(VariantHistory.PathIn(_mods)), Is.True);
            Assert.That(File.Exists(VariantHistory.PathIn(_mods) + ".tmp"), Is.False);
            Assert.That(loaded.Items.Values, Is.EquivalentTo(history.Items.Values));
            // Same variant id under two kinds: both kept, keyed by kind.
            Assert.That(loaded.Animals.Values, Is.EquivalentTo(history.Animals.Values));
            Assert.That(loaded.Animals, Has.Count.EqualTo(2));
            Assert.That(loaded.Unreadable, Is.False);
        });
    }

    [Test]
    public void ShouldRetireEverythingNotGeneratedThisTime()
    {
        var history = new VariantHistory();
        history.Remember([Item("lkone"), Item("lktwo")], [Animal("lkone"), Animal("lktwo")]);

        var (items, animals) = history.Retired([Item("lkone")], [Animal("lktwo")]);

        Assert.Multiple(() =>
        {
            Assert.That(items.Select(i => i.ItemId), Is.EqualTo(new[] { "test_lamp_lktwo" }));
            Assert.That(items, Has.All.Matches<ItemVariant>(i => i.Retired));
            Assert.That(animals.Select(a => a.VariantId), Is.EqualTo(new[] { "brown_lkone" }));
            Assert.That(animals, Has.All.Matches<AnimalVariant>(a => a.Retired));
        });

        var (allItems, allAnimals) = history.Retired([], []);
        Assert.That(allItems, Has.Count.EqualTo(2));
        Assert.That(allAnimals, Has.Count.EqualTo(2));
    }

    [Test]
    public void ShouldNeverStoreTheRetiredFlag()
    {
        var history = new VariantHistory();
        history.Remember([Item("lkone") with { Retired = true }], [Animal("lkone") with { Retired = true }]);

        Assert.That(history.Items.Values.Single().Retired, Is.False);
        Assert.That(history.Animals.Values.Single().Retired, Is.False);
    }

    [Test]
    public void ShouldStartEmptyWithoutAFileOrFolder()
    {
        Assert.That(VariantHistory.Load(_mods).Items, Is.Empty);
        Assert.That(VariantHistory.Load(null).Items, Is.Empty);
        Assert.That(VariantHistory.Load(Path.Combine(_mods, "missing")).Items, Is.Empty);

        // Saving into a folder that does not exist does nothing rather than creating it.
        var history = new VariantHistory();
        history.Remember([Item("lkone")], []);
        history.Save(Path.Combine(_mods, "missing"));
        Assert.That(Directory.Exists(Path.Combine(_mods, "missing")), Is.False);
    }

    [Test]
    public void ShouldSkipIncompleteEntries()
    {
        File.WriteAllText(VariantHistory.PathIn(_mods), """
            {
              "items": [
                { "item": "a_lkone", "object": "a_lkone", "base_item": "a", "base_object": "a" },
                { "item": "b_lkone", "object": "b_lkone", "base_item": "b" },
                { "item": "", "object": "c_lkone", "base_item": "c", "base_object": "c" },
                42
              ],
              "animals": [
                { "kind": "cow", "variant": "brown_lkone", "base": "brown" },
                { "kind": "cow", "variant": "white_lkone" }
              ]
            }
            """);

        var loaded = VariantHistory.Load(_mods);

        Assert.That(loaded.Items.Keys, Is.EqualTo(new[] { "a_lkone" }));
        Assert.That(loaded.Animals.Values.Select(a => a.VariantId), Is.EqualTo(new[] { "brown_lkone" }));
    }

    [Test]
    public void ShouldKeepACopyOfAnUnreadableHistoryBeforeReplacingIt()
    {
        var path = VariantHistory.PathIn(_mods);
        File.WriteAllText(path, "{ \"items\": [ { \"item\": \"a_lkone\", ");

        var history = VariantHistory.Load(_mods);
        Assert.That(history.Unreadable, Is.True);
        Assert.That(history.Items, Is.Empty);

        history.Remember([Item("lkone")], []);
        history.Save(_mods);

        Assert.That(File.ReadAllText(path + ".unreadable"), Does.StartWith("{ \"items\": [ { \"item\": \"a_lkone\""));
        Assert.That(VariantHistory.Load(_mods).Items.Keys, Is.EqualTo(new[] { "test_lamp_lkone" }));
    }
}
