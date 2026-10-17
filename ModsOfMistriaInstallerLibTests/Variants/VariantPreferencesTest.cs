using System.Text.Json;
using Garethp.ModsOfMistriaInstallerLib.Variants;

namespace ModsOfMistriaInstallerLibTests.Variants;

// The game writes this file and AIM only reads it, so anything it cannot understand must fall
// back to the defaults rather than block an install.
[TestFixture]
public class VariantPreferencesTest
{
    private static VariantPreferences Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return VariantPreferences.FromJson(doc.RootElement);
    }

    [Test]
    public void ShouldReadEveryChoice()
    {
        var preferences = Parse("""
            {
              "global_looks": { "furniture:test_lamp": "lkwitchy1234", "animal:cow": "vanilla" },
              "install": {
                "keep_original_looks": false,
                "variant_items": false,
                "variant_items_original": true,
                "variant_animals": false
              }
            }
            """);

        Assert.Multiple(() =>
        {
            Assert.That(preferences.GlobalLooks, Is.EqualTo(new Dictionary<string, string>
            {
                ["furniture:test_lamp"] = "lkwitchy1234",
                ["animal:cow"] = "vanilla",
            }));
            Assert.That(preferences.KeepOriginalLooks, Is.False);
            Assert.That(preferences.VariantItems, Is.False);
            Assert.That(preferences.VariantItemsOriginal, Is.True);
            Assert.That(preferences.VariantAnimals, Is.False);
        });
    }

    [Test]
    public void ShouldKeepTheDefaultsForAnythingMissingOrMistyped()
    {
        var preferences = Parse("""
            {
              "global_looks": { "furniture:a": "lkone", "furniture:b": 3, "furniture:c": "  ", "furniture:d": null },
              "install": { "variant_items": "no", "variant_animals": 0 }
            }
            """);

        Assert.Multiple(() =>
        {
            Assert.That(preferences.GlobalLooks, Is.EqualTo(new Dictionary<string, string> { ["furniture:a"] = "lkone" }));
            Assert.That(preferences.KeepOriginalLooks, Is.True);
            Assert.That(preferences.VariantItems, Is.True);
            Assert.That(preferences.VariantItemsOriginal, Is.False);
            Assert.That(preferences.VariantAnimals, Is.True);
        });
    }

    [TestCase("[]")]
    [TestCase("42")]
    [TestCase("\"text\"")]
    [TestCase("null")]
    [TestCase("{ \"global_looks\": [1, 2], \"install\": \"yes\" }")]
    public void ShouldFallBackToDefaultsForTheWrongShape(string json)
    {
        var preferences = Parse(json);

        Assert.Multiple(() =>
        {
            Assert.That(preferences.GlobalLooks, Is.Empty);
            Assert.That(preferences.KeepOriginalLooks, Is.True);
            Assert.That(preferences.VariantItems, Is.True);
            Assert.That(preferences.VariantItemsOriginal, Is.False);
            Assert.That(preferences.VariantAnimals, Is.True);
        });
    }

    [Test]
    public void ShouldLoadDefaultsFromAMissingOrDamagedFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "aim_prefs_" + Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "mistria_variants.json");
            Assert.That(VariantPreferences.Load(path).GlobalLooks, Is.Empty, "missing file");

            File.WriteAllText(path, "{ this is not json");
            var damaged = VariantPreferences.Load(path);
            Assert.That(damaged.GlobalLooks, Is.Empty);
            Assert.That(damaged.VariantItems, Is.True);

            // The game's writer may leave comments and trailing commas; both are tolerated.
            File.WriteAllText(path, "{ // written by the game\n \"global_looks\": { \"furniture:x\": \"lkone\", }, }");
            Assert.That(VariantPreferences.Load(path).GlobalLooks["furniture:x"], Is.EqualTo("lkone"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
