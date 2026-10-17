using Garethp.ModsOfMistriaInstallerLib.ModTypes;
using Garethp.ModsOfMistriaInstallerLib.Variants;
using ModsOfMistriaInstallerLibTests.Fixtures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using static ModsOfMistriaInstallerLibTests.Variants.VariantTestGame;

namespace ModsOfMistriaInstallerLibTests.Variants;

// A barn door opens by playing a sprite the engine finds by name: obj_door swaps the first
// "_closed" in the closed sprite's name for "_swing". A look renames the closed sprite, so every
// look must also have a swing sprite under the derived name, or the door cannot open (and
// string_to_asset on the missing name can stop the game).
[TestFixture]
public class VariantDoorTest
{
    private const string Folder = "Placeables/Buildings/Test";
    private const string Barn = "spr_placeable_test_barn_spring";
    private const string Closed = "spr_placeable_test_barn_door_spring_closed";
    private const string Swing = "spr_placeable_test_barn_door_spring_swing";
    private const string Floor = "spr_placeable_test_barn_door_doorway_floor_closed";
    private const string FloorSwing = "spr_placeable_test_barn_door_doorway_floor_swing";

    private static readonly Rgba32 Black = new(20, 20, 20, 255);
    private static readonly Rgba32 Purple = new(90, 30, 120, 255);
    private static readonly Rgba32 Red = new(200, 40, 40, 255);

    private PristineAssets _pristine = null!;
    private MockMod _framework = null!;

    [SetUp]
    public void BuildGame()
    {
        var game = new VariantTestGame();
        game.Text("assets/fiddle/object_prototypes/building.toml",
            "[test_barn]\nname = \"Test Barn\"\nsize = [14, 10]\n" +
            "door_closed = [\"" + Closed + "\"]\n" +
            "doorway_floor = [\"" + Floor + "\"]\n" +
            "[test_barn.sprites]\nspring = [\"" + Barn + "\"]\n");
        game.Sprite(Folder, Barn, 16, 16, Red);
        game.Sprite(Folder, Closed, 16, 16, Red);
        game.Sprite(Folder, Swing, 16, 16, Red);
        game.Sprite(Folder, Floor, 16, 16, Red);
        game.Sprite(Folder, FloorSwing, 16, 16, Red);
        _pristine = game.Build();
        _framework = Framework();
    }

    [TearDown]
    public void DisposeGame() => _pristine.Dispose();

    private VariantPlan Plan(params IMod[] mods) =>
        VariantPlanner.Plan(mods, _framework, _pristine, FamilyIndex.Build(_pristine), new VariantPreferences());

    private static string Look(IMod mod) => VariantFramework.LookIdForMod(mod.GetId());

    [Test]
    public void ShouldDeriveTheSwingNameTheWayTheEngineDoes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(VariantPlanner.DoorSwingName("spr_a_door_spring_closed"), Is.EqualTo("spr_a_door_spring_swing"));
            Assert.That(VariantPlanner.DoorSwingName("spr_a_door_spring_closed_lkx1"), Is.EqualTo("spr_a_door_spring_swing_lkx1"));
            // GML string_replace swaps the first occurrence only.
            Assert.That(VariantPlanner.DoorSwingName("spr_a_closed_b_closed"), Is.EqualTo("spr_a_swing_b_closed"));
            Assert.That(VariantPlanner.DoorSwingName("spr_a_door_open"), Is.Null);
            Assert.That(VariantPlanner.DoorClosedName("spr_a_door_spring_swing_lkx1"), Is.EqualTo("spr_a_door_spring_closed_lkx1"));
        });
    }

    [Test]
    public void ShouldCountADoorsSwingAsTheBuildingsArt()
    {
        var index = FamilyIndex.Build(_pristine);
        var family = index.FamilyOf(Swing);
        Assert.That(family, Is.Not.Null);
        Assert.That(family!.Id, Is.EqualTo("building:test_barn"));
        Assert.That(family.DoorSprites, Is.EqualTo(new[] { Closed }));
        // The doorway floor is named "..._closed" too, and the game even has a "_swing" of it,
        // but the engine sets the floor directly: it is not a door.
        Assert.That(index.FamilyOf(Floor)!.Id, Is.EqualTo("building:test_barn"));
        Assert.That(index.FamilyOf(FloorSwing), Is.Null);
    }

    [Test]
    public void ShouldPackBothDoorSpritesWhenAModReplacesThem()
    {
        var black = ReplacingMod("a.black_barns", "Black Barns", Black, Closed, Swing);
        var purple = ReplacingMod("b.purple_barns", "Purple Barns", Purple, Closed, Swing);
        var family = Plan(_framework, black, purple).RuntimeFamilies.Single();

        var look = family.LookById(Look(black))!;
        Assert.Multiple(() =>
        {
            Assert.That(look.Map[Closed], Is.EqualTo(Closed + "_" + Look(black)));
            Assert.That(look.Map[Swing], Is.EqualTo(Swing + "_" + Look(black)));
            Assert.That(VariantPlanner.DoorSwingName(look.Map[Closed]), Is.EqualTo(look.Map[Swing]));
        });
    }

    [Test]
    public void ShouldGiveEveryLookADoorThatCanOpen()
    {
        // Black recolours the closed door only; Purple (last, so the default) both sprites.
        var black = ReplacingMod("a.black_barns", "Black Barns", Black, Closed);
        var purple = ReplacingMod("b.purple_barns", "Purple Barns", Purple, Closed, Swing);
        var plan = Plan(_framework, black, purple);
        var family = plan.RuntimeFamilies.Single();
        Assert.That(family.InstallDefault, Is.EqualTo(Look(purple)));

        foreach (var look in family.Looks.Where(l => l.Look.Id != family.InstallDefault))
        {
            var closed = look.Resolve(Closed);
            var swing = look.Resolve(Swing);
            Assert.That(VariantPlanner.DoorSwingName(closed), Is.EqualTo(swing),
                $"{look.Look.Id}: the door it shows must find the swing it shows");
        }

        // Black's door swings with the game's own swing art (Black did not change it), packed
        // under the name its closed door derives.
        var blackSwing = Swing + "_" + Look(black);
        Assert.That(family.LookById(Look(black))!.Map[Swing], Is.EqualTo(blackSwing));
        var added = plan.Rewrites.Values.SelectMany(r => r.Added).ToDictionary(kv => kv.Key, kv => kv.Value);
        var png = added[$"animations/{Folder}/{blackSwing}.png"].Bytes!();
        using var image = Image.Load<Rgba32>(png);
        Assert.That(image[3, 3], Is.EqualTo(Red));
    }

    [Test]
    public void ShouldLeaveDoorwayFloorsAlone()
    {
        var black = ReplacingMod("a.black_barns", "Black Barns", Black, Floor);
        var purple = ReplacingMod("b.purple_barns", "Purple Barns", Purple, Floor);
        var plan = Plan(_framework, black, purple);
        var family = plan.RuntimeFamilies.Single();
        Assert.That(family.Looks.SelectMany(l => l.Map.Keys), Has.None.EqualTo(FloorSwing));
        Assert.That(plan.Warnings, Is.Empty);
    }

    [Test]
    public void ShouldGiveTheOriginalLookAMatchingSwing()
    {
        // The default recolours only the closed door, so the Original look shows a copy of the
        // game's closed door; its derived swing name must exist too.
        var black = ReplacingMod("a.black_barns", "Black Barns", Black, Closed, Swing);
        var purple = ReplacingMod("b.purple_barns", "Purple Barns", Purple, Closed);
        var plan = Plan(_framework, black, purple);
        var family = plan.RuntimeFamilies.Single();

        var original = family.LookById(VariantFramework.VanillaLook)!;
        var closed = original.Resolve(Closed);
        Assert.That(closed, Is.EqualTo(Closed + "_vanilla"));
        Assert.That(original.Resolve(Swing), Is.EqualTo(Swing + "_vanilla"));
        var added = plan.Rewrites.Values.SelectMany(r => r.Added).Select(kv => kv.Key).ToHashSet();
        Assert.That(added, Does.Contain($"animations/{Folder}/{Swing}_vanilla.png"));
    }
}
