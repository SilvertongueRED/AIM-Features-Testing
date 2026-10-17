using Garethp.ModsOfMistriaInstallerLib.Variants;
using ModsOfMistriaInstallerLibTests.Fixtures;

namespace ModsOfMistriaInstallerLibTests.Variants;

// Look ids end up inside item ids, object ids, animal keys, sprite names and save files, so the
// naming rules are a persistent contract: these tests pin them.
[TestFixture]
public class VariantFrameworkTest
{
    [Test]
    public void ShouldGiveAModTheSameLookIdEveryTime()
    {
        var first = VariantFramework.LookIdForMod("alice.witchy_lamps");
        var second = VariantFramework.LookIdForMod("alice.witchy_lamps");

        Assert.That(first, Is.EqualTo(second));
        // Pinned: a change here renames every generated item in every player's save.
        Assert.That(first, Is.EqualTo("lkwitchylamps" + VariantFramework.Hash4("alice.witchy_lamps")));
    }

    [Test]
    public void ShouldIgnoreCaseAndSurroundingWhitespaceInTheModId()
    {
        Assert.That(VariantFramework.LookIdForMod("  Alice.Witchy_Lamps "),
            Is.EqualTo(VariantFramework.LookIdForMod("alice.witchy_lamps")));
    }

    [TestCase("alice.witchy_lamps")]
    [TestCase("bob.Pastel Lamps!!")]
    [TestCase("no_dot_in_this_id")]
    [TestCase("x.123_starts_with_digits")]
    [TestCase("j.doe.has.more.dots")]
    [TestCase("")]
    [TestCase("a.名前")]
    [TestCase("a.a_very_long_mod_name_that_goes_on_and_on_forever")]
    public void ShouldProduceACanonicalUnderscoreFreeSegment(string modId)
    {
        var look = VariantFramework.LookIdForMod(modId);

        Assert.Multiple(() =>
        {
            Assert.That(look, Does.StartWith("lk"));
            Assert.That(look, Does.Not.Contain("_"));
            Assert.That(VariantFramework.IsCanonicalId(look), Is.True, look);
            // Appended to a canonical id, it must stay canonical.
            Assert.That(VariantFramework.IsCanonicalId("cottage_garden_arch_" + look), Is.True);
            // lk + at most 16 name characters + 4 hash characters
            Assert.That(look.Length, Is.LessThanOrEqualTo(2 + 16 + 4));
        });
    }

    [Test]
    public void ShouldGiveDifferentModsDifferentLookIds()
    {
        string[] ids =
        [
            "alice.witchy_lamps", "bob.witchy_lamps", "alice.witchy_lamps_2", "alice.pastel_lamps",
            "carol.witchy_lamps", "alice.witchylamps",
        ];

        var looks = ids.Select(VariantFramework.LookIdForMod).ToList();

        Assert.That(looks, Is.Unique);
    }

    [Test]
    public void ShouldHashToFourLowercaseBase36Characters()
    {
        foreach (var text in new[] { "", "a", "alice.witchy_lamps", "名前", new string('z', 500) })
        {
            var hash = VariantFramework.Hash4(text);
            Assert.That(hash, Has.Length.EqualTo(4));
            Assert.That(hash, Does.Match("^[0-9a-z]{4}$"));
            Assert.That(VariantFramework.Hash4(text), Is.EqualTo(hash), "stable");
        }

        Assert.That(VariantFramework.Hash4("a"), Is.Not.EqualTo(VariantFramework.Hash4("b")));
    }

    [Test]
    public void ShouldUseStandardFnv1A()
    {
        // The published FNV-1a 32-bit test vectors.
        Assert.That(VariantFramework.Fnv1A(""), Is.EqualTo(0x811c9dc5u));
        Assert.That(VariantFramework.Fnv1A("a"), Is.EqualTo(0xe40c292cu));
        Assert.That(VariantFramework.Fnv1A("foobar"), Is.EqualTo(0xbf9cf968u));
    }

    [TestCase("cottage_garden_arch", true)]
    [TestCase("lamp", true)]
    [TestCase("lamp_v2", true)]
    [TestCase("lamp_lkabc1x9z", true)]
    [TestCase("", false)]
    [TestCase("Lamp", false)]
    [TestCase("lamp__double", false)]
    [TestCase("_lamp", false)]
    [TestCase("lamp_", false)]
    [TestCase("lamp_2nd", false)]
    [TestCase("2lamp", false)]
    [TestCase("lamp-v2", false)]
    [TestCase("lamp v2", false)]
    public void ShouldRecogniseCanonicalSnakeCase(string id, bool expected)
    {
        Assert.That(VariantFramework.IsCanonicalId(id), Is.EqualTo(expected));
    }

    [Test]
    public void ShouldIdentifyTheFrameworkByItsMarkerFile()
    {
        var framework = new MockMod(new Dictionary<string, object> { [VariantFramework.MarkerFile] = "" });
        var other = new MockMod(new Dictionary<string, object> { ["manifest.json"] = "{}" })
        {
            Name = "Variants Framework", // the name alone does not make it the framework
        };

        Assert.That(VariantFramework.IsFrameworkMod(framework), Is.True);
        Assert.That(VariantFramework.IsFrameworkMod(other), Is.False);
    }

    [Test]
    public void ShouldNameLookSpritesAfterTheVanillaSprite()
    {
        Assert.That(VariantFramework.LookSpriteName("spr_decor_lamp_south", "lkabc1234"),
            Is.EqualTo("spr_decor_lamp_south_lkabc1234"));
        Assert.That(VariantFramework.VariantDisplayName("Lamp", "Witchy Lamps"), Is.EqualTo("Lamp (Witchy Lamps)"));
    }

    [Test]
    public void ShouldReadTheRuntimeConfigFromTheOverriddenDataFolder()
    {
        var previous = Environment.GetEnvironmentVariable("AIM_FOM_DATA_DIR");
        try
        {
            Environment.SetEnvironmentVariable("AIM_FOM_DATA_DIR", "/tmp/fom-data");
            Assert.That(VariantFramework.RuntimeConfigPath(), Is.EqualTo(Path.Combine(
                "/tmp/fom-data", "mod_data", VariantFramework.RuntimeName, VariantFramework.RuntimeName + ".json")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AIM_FOM_DATA_DIR", previous);
        }
    }
}
