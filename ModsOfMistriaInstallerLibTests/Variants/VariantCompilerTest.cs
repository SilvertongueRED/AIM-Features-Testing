using Garethp.ModsOfMistriaInstallerLib.Variants;
using ModsOfMistriaInstallerLibTests.Fixtures;
using ModsOfMistriaInstallerLibTests.TestUtils;
using SixLabors.ImageSharp.PixelFormats;
using Tomlyn.Model;
using static ModsOfMistriaInstallerLibTests.Variants.VariantTestGame;
using Toml = Garethp.ModsOfMistriaInstallerLib.Utils.Toml;

namespace ModsOfMistriaInstallerLibTests.Variants;

// What one install gets from the framework, end to end from the pristine archive on disk - and,
// just as important, what it gets when the framework is gone but saves still hold its ids.
[TestFixture]
public class VariantCompilerTest
{
    private string _root = "";
    private string _mods = "";
    private string _zip = "";

    [SetUp]
    public void CreateGame()
    {
        _root = Path.Combine(Path.GetTempPath(), "aim_variants_" + Path.GetRandomFileName());
        _mods = Path.Combine(_root, "mods");
        Directory.CreateDirectory(_mods);
        _zip = WithLamp().WriteZip(_root);
    }

    [TearDown]
    public void RemoveGame()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private static MockMod Witchy() => ReplacingMod("alice.witchy_lamps", "Witchy Lamps", new Rgba32(1, 2, 3, 255), LampSprite);
    private static MockMod Pastel() => ReplacingMod("bob.pastel_lamps", "Pastel Lamps", new Rgba32(4, 5, 6, 255), LampSprite);

    private VariantSession? Prepare(params MockMod[] mods) =>
        VariantCompiler.Prepare(mods, _zip, _mods, (_, _) => { }, new VariantPreferences(), "test");

    private void WriteHistory()
    {
        var history = new VariantHistory();
        history.Remember(
            [new ItemVariant("furniture:test_lamp", "lkgone1234", "test_lamp", "test_lamp", "test_lamp_lkgone1234", "test_lamp_lkgone1234")],
            [new AnimalVariant("animal:cow", "lkgone1234", "cow", "brown", "brown_lkgone1234")]);
        history.Save(_mods);
    }

    [Test]
    public void ShouldLeaveAnInstallWithoutTheFrameworkAndWithoutHistoryAlone()
    {
        Assert.That(Prepare(Witchy(), Pastel()), Is.Null);
    }

    [Test]
    public void ShouldPlanLooksAndOverlaysWhenTheFrameworkIsSelected()
    {
        var framework = Framework();
        var witchy = Witchy();
        var pastel = Pastel();

        using var session = Prepare(framework, witchy, pastel);

        Assert.That(session, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(session!.RetireOnly, Is.False);
            Assert.That(session.Plan.RuntimeFamilies, Has.Count.EqualTo(1));
            Assert.That(session.ItemVariants, Has.Count.EqualTo(1));
            Assert.That(session.Effective(witchy), Is.InstanceOf<VariantOverlayMod>());
            Assert.That(session.Effective(pastel), Is.SameAs(pastel), "the default look's mod is read as it is");
            // The loser's art is read from its own look sprite instead of the shared one.
            Assert.That(session.Effective(witchy).FileExists($"images/replace/{LampSprite}.png"), Is.False);
            Assert.That(session.Effective(witchy).GetFilesInFolder("images/replace", ".png"), Is.Empty);
            Assert.That(session.Effective(witchy).GetAllFiles(".png"), Does.Contain(
                $"animations/{FurnitureFolder}/{LampSprite}_{VariantFramework.LookIdForMod(witchy.GetId())}.png"));
            Assert.That(session.Effective(framework).GetAllFiles(".png"), Does.Contain(
                $"animations/{FurnitureFolder}/{LampSprite}_vanilla.png"));
        });

        // The generated registry replaces the framework's stub through its overlay.
        var registry = session!.Effective(framework).ReadFile(VariantFramework.RegistryGmlPath);
        Assert.That(registry, Does.Contain($"function {VariantFramework.RegistryFunction}()"));
        Assert.That(registry, Does.Contain("installer: \"test\""));
    }

    [Test]
    public void ShouldRememberOnlyWhatActuallyLanded()
    {
        using var session = Prepare(Framework(), Witchy(), Pastel())!;
        session.WrittenItemVariants.AddRange(session.ItemVariants);

        session.Commit();

        var history = VariantHistory.Load(_mods);
        Assert.That(history.Items.Keys, Is.EqualTo(session.ItemVariants.Select(v => v.ItemId)));
    }

    [Test]
    public void ShouldKeepEarlierVariantsLoadableWhenTheFrameworkIsRemoved()
    {
        WriteHistory();

        using var session = Prepare(Witchy(), Pastel());

        Assert.That(session, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(session!.RetireOnly, Is.True);
            Assert.That(session.Plan.RuntimeFamilies, Is.Empty);
            Assert.That(session.Plan.Rewrites, Is.Empty, "no overlays, no registry, no look sprites");
            Assert.That(session.ItemVariants, Is.Empty);
            Assert.That(session.RetiredItems.Select(i => i.ItemId), Is.EqualTo(new[] { "test_lamp_lkgone1234" }));
            Assert.That(session.RetiredItems, Has.All.Matches<ItemVariant>(i => i.Retired));
            Assert.That(session.RetiredAnimals.Select(a => a.VariantId), Is.EqualTo(new[] { "brown_lkgone1234" }));
        });
    }

    [Test]
    public void ShouldWriteRetiredStandInsWithoutTheFramework()
    {
        WriteHistory();
        using var session = Prepare(Witchy(), Pastel())!;
        var files = new MockFileModifier(new Dictionary<string, string>
        {
            ["assets/fiddle/object_prototypes/furniture.toml"] =
                "[test_lamp]\nsize = [2, 2]\n[test_lamp.south]\nsprite = \"" + LampSprite + "\"\n",
            ["assets/fiddle/items/furniture/test_set.toml"] =
                "[test_lamp]\nname = \"Test Lamp\"\nicon_sprite = \"" + LampIcon + "\"\nobject = \"test_lamp\"\n",
            ["assets/fiddle/stores.toml"] = "[general]\nconstant_stock = [\"test_lamp\"]\n",
            ["assets/fiddle/ranching/animals/cow.toml"] = "[variants.default.female]\nkey = \"basic\"\n[variants.brown]\nname = \"Brown\"\n",
            [$"assets/animations/{FurnitureFolder}/{LampSprite}.meta.toml"] = SpriteMeta(16, 16),
            [$"assets/animations/{IconFolder}/{LampIcon}.meta.toml"] = SpriteMeta(8, 8),
        });

        VariantFinalizer.Apply(files, session, (_, _) => { });

        var furniture = Toml.ParseToml(files.GetFile("assets/fiddle/object_prototypes/furniture.toml"));
        var items = Toml.ParseToml(files.GetFile("assets/" + VariantFinalizer.VariantItemsFile));
        var cow = Toml.ParseToml(files.GetFile("assets/fiddle/ranching/animals/cow.toml"));
        Assert.Multiple(() =>
        {
            Assert.That(furniture.ContainsKey("test_lamp_lkgone1234"), Is.True);
            Assert.That(((TomlTable)items["test_lamp_lkgone1234"])["name"], Is.EqualTo("Test Lamp (retired look)"));
            Assert.That(((TomlTable)((TomlTable)cow["variants"])["brown_lkgone1234"])["acquirable"], Is.EqualTo(false));
            Assert.That(files.GetFile("assets/fiddle/stores.toml"), Does.Not.Contain("lkgone1234"));
            Assert.That(session.Plan.Warnings, Is.Empty);
        });
    }

    [Test]
    public void ShouldNotRewriteTheHistoryInARetireOnlyInstall()
    {
        WriteHistory();
        var path = VariantHistory.PathIn(_mods);
        var before = File.ReadAllText(path);
        File.SetLastWriteTimeUtc(path, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        using (var session = Prepare(Witchy())!)
        {
            session.WrittenItemVariants.AddRange(session.RetiredItems);
            session.Commit();
        }

        Assert.That(File.ReadAllText(path), Is.EqualTo(before));
        Assert.That(File.GetLastWriteTimeUtc(path), Is.EqualTo(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Test]
    public void ShouldFallBackToRetiringWhenThePristineArchiveIsMissing()
    {
        WriteHistory();

        using var session = VariantCompiler.Prepare([Framework(), Witchy()], Path.Combine(_root, "missing.zip"), _mods,
            (_, _) => { }, new VariantPreferences());

        Assert.That(session?.RetireOnly, Is.True);
        Assert.That(session!.RetiredItems, Has.Count.EqualTo(1));
    }

    [Test]
    public void ShouldRetireEverythingWhenTheFrameworkDoesNotMakeItIntoTheInstall()
    {
        // Planned with the framework, which the GML layer then skipped: no runtime to serve the
        // new variants, so only the ids earlier installs generated are kept loadable.
        WriteHistory();
        using var session = Prepare(Framework(), Witchy(), Pastel())!;
        Assert.That(session.ItemVariants, Is.Not.Empty);

        session.RetireEverything();

        Assert.Multiple(() =>
        {
            Assert.That(session.RetireOnly, Is.True);
            Assert.That(session.ItemVariants, Is.Empty);
            Assert.That(session.AnimalVariants, Is.Empty);
            Assert.That(session.RetiredItems.Select(i => i.ItemId), Is.EqualTo(new[] { "test_lamp_lkgone1234" }));
            Assert.That(session.RetiredAnimals, Has.Count.EqualTo(1));
        });
    }
}
