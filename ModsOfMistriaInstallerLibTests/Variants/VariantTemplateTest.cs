using Garethp.ModsOfMistriaInstallerLib.ModTypes;
using Garethp.ModsOfMistriaInstallerLib.Variants;
using ModsOfMistriaInstallerLibTests.Fixtures;
using SixLabors.ImageSharp.PixelFormats;
using static ModsOfMistriaInstallerLibTests.Variants.VariantTestGame;

namespace ModsOfMistriaInstallerLibTests.Variants;

// A prototype file's [default] table is the template every entry inherits from (crop.toml's
// mound sprite, for one), not an object the game can place. Its art is shared by every entry,
// so it must not become a switchable thing of its own (the game has no "default" object to
// restyle); overlapping mods there are an install-time choice like any other shared art.
[TestFixture]
public class VariantTemplateTest
{
    private const string Folder = "Crops and Forageables/Test";
    private const string Mound = "spr_forageable_test_mound_spring";
    private const string Turnip = "spr_crop_test_turnip";

    private static readonly Rgba32 Black = new(20, 20, 20, 255);
    private static readonly Rgba32 Purple = new(90, 30, 120, 255);
    private static readonly Rgba32 Green = new(40, 160, 60, 255);

    private PristineAssets _pristine = null!;
    private MockMod _framework = null!;

    [SetUp]
    public void BuildGame()
    {
        var game = new VariantTestGame();
        game.Text("assets/fiddle/object_prototypes/crop.toml",
            "[default]\nsprites = [\"spr_illegal_16\"]\nmound_sprite = \"" + Mound + "\"\n" +
            "[test_turnip]\nsprites = [\"" + Turnip + "\"]\n");
        game.Sprite(Folder, Mound, 16, 16, Green);
        game.Sprite(Folder, Turnip, 16, 16, Green);
        _pristine = game.Build();
        _framework = Framework();
    }

    [TearDown]
    public void DisposeGame() => _pristine.Dispose();

    private VariantPlan Plan(params IMod[] mods) =>
        VariantPlanner.Plan(mods, _framework, _pristine, FamilyIndex.Build(_pristine), new VariantPreferences());

    [Test]
    public void ShouldNotMakeTheTemplateAThing()
    {
        var index = FamilyIndex.Build(_pristine);
        Assert.Multiple(() =>
        {
            Assert.That(index.FamilyOf(Mound), Is.Null);
            Assert.That(index.FamilyOf(Turnip)!.Id, Is.EqualTo("world:test_turnip"));
        });
    }

    [Test]
    public void ShouldOfferSharedTemplateArtAtInstallTime()
    {
        var black = ReplacingMod("a.black_mounds", "Black Mounds", Black, Mound);
        var purple = ReplacingMod("b.purple_mounds", "Purple Mounds", Purple, Mound);
        var plan = Plan(_framework, black, purple);
        Assert.Multiple(() =>
        {
            Assert.That(plan.RuntimeFamilies.Select(f => f.Family.Id), Has.None.EqualTo("world:default"));
            Assert.That(plan.InstallOnlyFamilies, Has.Count.EqualTo(1));
            Assert.That(plan.InstallOnlyFamilies[0].Family.Sprites, Does.Contain(Mound));
            Assert.That(plan.Warnings, Is.Empty);
        });
    }
}
