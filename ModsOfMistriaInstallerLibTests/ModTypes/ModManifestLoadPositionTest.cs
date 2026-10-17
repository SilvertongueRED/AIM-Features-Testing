using Garethp.ModsOfMistriaInstallerLib.ModTypes;
using Newtonsoft.Json.Linq;
using Tomlyn;
using Tomlyn.Model;

namespace ModsOfMistriaInstallerLibTests.ModTypes;

// The manifest's load_position: a hint about where a mod sits among mods of its own kind.
public class ModManifestLoadPositionTest
{
    private static ModLoadPosition FromJson(string json) => ModManifest.FromJson(JObject.Parse(json)).LoadPosition;

    private static ModLoadPosition FromToml(string toml) =>
        ModManifest.FromToml(TomlSerializer.Deserialize<TomlTable>(toml)!).LoadPosition;

    [Test]
    public void JsonReadsEarlyAndLateWhateverTheCase()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FromJson("""{ "name": "A", "load_position": "early" }"""), Is.EqualTo(ModLoadPosition.Early));
            Assert.That(FromJson("""{ "name": "A", "load_position": " LATE " }"""), Is.EqualTo(ModLoadPosition.Late));
        });
    }

    [Test]
    public void TomlReadsEarly()
    {
        Assert.That(FromToml("""
            name = "A"
            load_position = "early"
            """), Is.EqualTo(ModLoadPosition.Early));
    }

    [Test]
    public void AMissingOrUnknownValueIsNoPreference()
    {
        // A typo or a value from a newer AIM must never cost the mod anything.
        Assert.Multiple(() =>
        {
            Assert.That(FromJson("""{ "name": "A" }"""), Is.EqualTo(ModLoadPosition.Default));
            Assert.That(FromJson("""{ "name": "A", "load_position": "sideways" }"""), Is.EqualTo(ModLoadPosition.Default));
            Assert.That(FromJson("""{ "name": "A", "load_position": 3 }"""), Is.EqualTo(ModLoadPosition.Default));
            Assert.That(FromToml("""name = "A" """), Is.EqualTo(ModLoadPosition.Default));
        });
    }
}
