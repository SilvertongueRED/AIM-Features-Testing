using Garethp.ModsOfMistriaInstallerLib.Variants;
using SixLabors.ImageSharp.PixelFormats;

namespace ModsOfMistriaInstallerLibTests.Variants;

// spr_animal_{kind}_{key}_{anim}_{dir}: the key is what a variant points at, so splitting the
// name wrongly would give a look family the wrong sprite names and the engine would refuse the
// animal at boot.
[TestFixture]
public class AnimalSpriteNameTest
{
    private static readonly string[] HorseAnimations = ["playball", "idle_2", "blink", "happy", "sleep", "idle", "walk", "jump", "run", "eat"];

    [Test]
    public void ShouldSplitKindKeyAnimationAndDirection()
    {
        Assert.That(AnimalSpriteName.TryParse("spr_animal_horse_basic_base_idle_east", "horse", HorseAnimations, out var parts), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(parts.Kind, Is.EqualTo("horse"));
            Assert.That(parts.Key, Is.EqualTo("basic_base"));
            Assert.That(parts.Animation, Is.EqualTo("idle"));
            Assert.That(parts.Direction, Is.EqualTo("east"));
        });
    }

    [Test]
    public void ShouldPreferTheLongestAnimation()
    {
        // idle_2 must win over idle (which does not even match here) and, in the ambiguous case
        // below, deep_sleep over sleep: with sleep first the key would come out as "basic_deep".
        Assert.That(AnimalSpriteName.TryParse("spr_animal_horse_basic_base_idle_2_north", "horse", HorseAnimations, out var idle2), Is.True);
        Assert.That((idle2.Key, idle2.Animation), Is.EqualTo(("basic_base", "idle_2")));

        string[] longestFirst = ["deep_sleep", "sleep"];
        Assert.That(AnimalSpriteName.TryParse("spr_animal_cow_basic_deep_sleep_south", "cow", longestFirst, out var cow), Is.True);
        Assert.That((cow.Key, cow.Animation), Is.EqualTo(("basic", "deep_sleep")));

        // The caller owns the order; this is why FamilyIndex hands the list over longest first.
        string[] shortestFirst = ["sleep", "deep_sleep"];
        Assert.That(AnimalSpriteName.TryParse("spr_animal_cow_basic_deep_sleep_south", "cow", shortestFirst, out var wrong), Is.True);
        Assert.That(wrong.Key, Is.EqualTo("basic_deep"));
    }

    [TestCase("spr_animal_cow_basic_idle_west")]   // not a direction the engine uses
    [TestCase("spr_animal_cow_basic_dance_east")]  // unknown animation
    [TestCase("spr_animal_cow_idle_east")]         // no key left
    [TestCase("spr_animal_chicken_basic_idle_east")] // another kind
    [TestCase("spr_ui_icon_animal_cow_basic")]
    public void ShouldRefuseNamesThatAreNotThisKindsBodySprites(string sprite)
    {
        Assert.That(AnimalSpriteName.TryParse(sprite, "cow", ["idle", "walk"], out _), Is.False);
    }

    [Test]
    public void ShouldRebuildTheNameAroundANewKey()
    {
        AnimalSpriteName.TryParse("spr_animal_horse_basic_base_idle_2_north", "horse", HorseAnimations, out var parts);

        Assert.That(parts.WithKey("basic_base_lkabc1234"), Is.EqualTo("spr_animal_horse_basic_base_lkabc1234_idle_2_north"));
    }

    [Test]
    public void ShouldIndexAnimalAnimationsLongestFirst()
    {
        var game = new VariantTestGame();
        game.Text("assets/fiddle/ranching/animals/default.toml", "[animations.idle]\nx = 1\n[animations.sleep]\nx = 1\n");
        game.Text("assets/fiddle/ranching/animals/cow.toml",
            "[core]\nname = \"Cow\"\n[animations.deep_sleep]\nx = 1\n[animations.walk]\nx = 1\n");
        game.Sprite("Animals/Cow", "spr_animal_cow_basic_idle_east", 16, 16, new Rgba32(1, 2, 3, 255));
        game.Sprite("Animals/Cow", "spr_animal_cow_basic_lut", 4, 1, new Rgba32(1, 2, 3, 255));

        var index = FamilyIndex.Build(game.Build());

        Assert.That(index.AnimalAnimations["cow"], Is.EqualTo(new[] { "deep_sleep", "sleep", "idle", "walk" }));
        var family = index.FamilyOf("spr_animal_cow_basic_idle_east");
        Assert.That(family?.Id, Is.EqualTo("animal:cow"));
        Assert.That(family!.DisplayName, Is.EqualTo("Cow"));
        // LUT palettes are merged by the installer, never a look.
        Assert.That(index.FamilyOf("spr_animal_cow_basic_lut"), Is.Null);
    }
}
