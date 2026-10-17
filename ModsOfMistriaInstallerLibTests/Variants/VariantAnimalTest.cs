using Garethp.ModsOfMistriaInstallerLib.Variants;
using ModsOfMistriaInstallerLibTests.Fixtures;
using ModsOfMistriaInstallerLibTests.TestUtils;
using SixLabors.ImageSharp.PixelFormats;
using Tomlyn.Model;
using static ModsOfMistriaInstallerLibTests.Variants.VariantTestGame;
using Toml = Garethp.ModsOfMistriaInstallerLib.Utils.Toml;

namespace ModsOfMistriaInstallerLibTests.Variants;

// Two mods repaint the adult cow. The engine builds an animal's sprite names from its variant's
// key (spr_animal_cow_{key}_{anim}_{dir}) and refuses an animal whose animation set is
// incomplete, so a look can only become an adoption variant when it covers every sprite of a key.
[TestFixture]
public class VariantAnimalTest
{
    private const string CowToml = """
        [core]
        name = "Cow"

        [animations.idle]
        valid_for = ["male", "female", "baby"]

        [variants.default]
        [variants.default.female]
        key = "basic"
        [variants.default.male]
        key = "basic"
        [variants.default.baby]
        key = "basic_baby"
        top_key = "<n/a>"

        [variants.brown]
        name = "Brown Cow"
        default_unlocked = true
        born_in = ["spring"]

        [variants.prize]
        name = "Prize Cow"
        acquirable = false
        """;

    private static readonly string[] AdultSprites = ["spr_animal_cow_basic_idle_east", "spr_animal_cow_basic_idle_south"];

    private PristineAssets _pristine = null!;
    private MockMod _framework = null!;
    private MockMod _spotty = null!;
    private MockMod _highland = null!;
    private string _spottyLook = "";

    [SetUp]
    public void BuildGame()
    {
        var game = new VariantTestGame();
        game.Text("assets/fiddle/ranching/animals/default.toml", "[animations.idle]\nx = 1\n");
        game.Text("assets/fiddle/ranching/animals/cow.toml", CowToml);
        foreach (var sprite in AdultSprites.Append("spr_animal_cow_basic_baby_idle_east"))
            game.Sprite("Animals/Cow", sprite, 16, 16, new Rgba32(120, 80, 40, 255));
        game.Sprite("UI/Icons/Animals", "spr_ui_icon_animal_cow_basic", 8, 8, new Rgba32(120, 80, 40, 255), atlas: "UI");
        _pristine = game.Build();

        _framework = Framework();
        _spotty = ReplacingMod("alice.spotty_cows", "Spotty Cows", new Rgba32(255, 255, 255, 255), AdultSprites);
        _highland = ReplacingMod("bob.highland_cows", "Highland Cows", new Rgba32(160, 60, 20, 255), AdultSprites);
        _spottyLook = VariantFramework.LookIdForMod(_spotty.GetId());
    }

    [TearDown]
    public void DisposeGame() => _pristine.Dispose();

    private VariantPlan Plan(params MockMod[] contentMods) =>
        VariantPlanner.Plan([_framework, .. contentMods], _framework, _pristine, FamilyIndex.Build(_pristine),
            new VariantPreferences());

    [Test]
    public void ShouldNameLookSpritesWithTheLookInsideTheKey()
    {
        var plan = Plan(_spotty, _highland);
        var family = plan.RuntimeFamilies.Single();
        var spotty = family.LookById(_spottyLook)!;

        Assert.Multiple(() =>
        {
            Assert.That(family.Family.Id, Is.EqualTo("animal:cow"));
            Assert.That(family.Family.Kind, Is.EqualTo(FamilyKind.Animal));
            Assert.That(spotty.Map[AdultSprites[0]], Is.EqualTo($"spr_animal_cow_basic_{_spottyLook}_idle_east"));
            Assert.That(spotty.Map[AdultSprites[1]], Is.EqualTo($"spr_animal_cow_basic_{_spottyLook}_idle_south"));
            Assert.That(family.LookById("vanilla")!.Map[AdultSprites[0]], Is.EqualTo("spr_animal_cow_basic_vanilla_idle_east"));
            Assert.That(plan.Rewrites[_spotty.GetId()].Added.Keys,
                Does.Contain($"animations/Animals/Cow/spr_animal_cow_basic_{_spottyLook}_idle_east.png"));
        });
    }

    [Test]
    public void ShouldOfferAnAdoptionVariantForEachAcquirableVariantOfAFullyCoveredKey()
    {
        var plan = Plan(_spotty, _highland);

        var (items, animals) = VariantContentPlanner.Plan(plan, _pristine);

        Assert.That(items, Is.Empty);
        // "prize" cannot be adopted, so it gets no look variant; the default look (Highland, last
        // in load order) is what the base sprites show and needs none either.
        Assert.That(animals, Is.EqualTo(new[]
        {
            new AnimalVariant("animal:cow", _spottyLook, "cow", "brown", $"brown_{_spottyLook}"),
        }));

        var family = plan.RuntimeFamilies.Single();
        var variant = AnimalVariants.Read(Toml.ParseToml(CowToml)).Single(v => v.Key == "brown");
        var types = AnimalVariants.ForLook(variant, family.LookById(_spottyLook)!, family.Family, plan)!;
        Assert.Multiple(() =>
        {
            Assert.That(types["female"].Key, Is.EqualTo($"basic_{_spottyLook}"));
            Assert.That(types["male"].Key, Is.EqualTo($"basic_{_spottyLook}"));
            Assert.That(types["baby"].Key, Is.EqualTo("basic_baby"), "the calf was not repainted");
            Assert.That(types["baby"].TopKey, Is.Null);
            Assert.That(types["female"].UiKey, Is.EqualTo("basic"), "the icon was not repainted either");
        });
    }

    [Test]
    public void ShouldNotOfferAVariantForAPartlyCoveredKey()
    {
        // Only one direction repainted: a cow built from that key would mix two looks.
        var partial = ReplacingMod("carol.half_cows", "Half Cows", new Rgba32(0, 0, 255, 255), AdultSprites[0]);

        var plan = Plan(partial, _highland);

        Assert.That(VariantContentPlanner.Plan(plan, _pristine).Animals, Is.Empty);
        Assert.That(plan.RuntimeFamilies.Single().Looks, Has.Count.EqualTo(3), "still a look, just not an adoption variant");
    }

    [Test]
    public void ShouldWriteTheAdoptionVariantPointingAtTheLooksKey()
    {
        var plan = Plan(_spotty, _highland);
        var (items, animals) = VariantContentPlanner.Plan(plan, _pristine);
        var session = VariantSession.Create(plan, null, new VariantHistory(), null, items, animals, retireOnly: false);
        var archive = new Dictionary<string, string> { ["assets/fiddle/ranching/animals/cow.toml"] = CowToml };
        foreach (var sprite in AdultSprites)
        {
            archive[$"assets/animations/Animals/Cow/{sprite}.meta.toml"] = SpriteMeta(16, 16);
            archive[$"assets/animations/Animals/Cow/{session.Plan.FindLook("animal:cow", _spottyLook)!.Map[sprite]}.meta.toml"] = SpriteMeta(16, 16);
        }

        var files = new MockFileModifier(archive);
        VariantFinalizer.Apply(files, session, (_, _) => { });

        var cow = Toml.ParseToml(files.GetFile("assets/fiddle/ranching/animals/cow.toml"));
        var variants = (TomlTable)cow["variants"];
        var written = (TomlTable)variants[$"brown_{_spottyLook}"];
        var female = (TomlTable)written["female"];
        var baby = (TomlTable)written["baby"];
        Assert.Multiple(() =>
        {
            Assert.That(written["name"], Is.EqualTo("Brown Cow (Spotty Cows)"));
            Assert.That(written["acquirable"], Is.EqualTo(true));
            Assert.That((TomlArray)written["born_in"], Is.Empty, "never born from breeding");
            Assert.That(written.ContainsKey("default_unlocked"), Is.False);
            Assert.That(female["key"], Is.EqualTo($"basic_{_spottyLook}"));
            Assert.That(female["top_key"], Is.EqualTo("<n/a>"));
            Assert.That(female["ui_key"], Is.EqualTo("basic"));
            Assert.That(baby["key"], Is.EqualTo("basic_baby"));
            Assert.That(session.WrittenAnimalVariants, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void ShouldNotWriteAnAdoptionVariantWhoseSpritesDidNotLand()
    {
        var plan = Plan(_spotty, _highland);
        var (items, animals) = VariantContentPlanner.Plan(plan, _pristine);
        var session = VariantSession.Create(plan, null, new VariantHistory(), null, items, animals, retireOnly: false);
        var files = new MockFileModifier(new Dictionary<string, string> { ["assets/fiddle/ranching/animals/cow.toml"] = CowToml });

        VariantFinalizer.Apply(files, session, (_, _) => { });

        Assert.That(files.GetFile("assets/fiddle/ranching/animals/cow.toml"), Is.EqualTo(CowToml));
        Assert.That(session.WrittenAnimalVariants, Is.Empty);
        Assert.That(session.Plan.Warnings, Has.Some.Contains($"'brown_{_spottyLook}' was not added"));
    }

    [Test]
    public void ShouldKeepAKnownAdoptionVariantLoadableWhenItsSpritesDidNotLand()
    {
        // Saves may already hold a cow of this variant (an earlier install made it): an unknown
        // variant would stop the save from loading, so it stays as a retired stand-in.
        var plan = Plan(_spotty, _highland);
        var (items, animals) = VariantContentPlanner.Plan(plan, _pristine);
        var history = new VariantHistory();
        history.Remember([], animals);
        var session = VariantSession.Create(plan, null, history, null, items, animals, retireOnly: false);
        var files = new MockFileModifier(new Dictionary<string, string> { ["assets/fiddle/ranching/animals/cow.toml"] = CowToml });

        VariantFinalizer.Apply(files, session, (_, _) => { });

        var variants = (TomlTable)Toml.ParseToml(files.GetFile("assets/fiddle/ranching/animals/cow.toml"))["variants"];
        var retired = (TomlTable)variants[$"brown_{_spottyLook}"];
        Assert.Multiple(() =>
        {
            Assert.That(retired["acquirable"], Is.EqualTo(false));
            Assert.That(retired["name"], Is.EqualTo("Brown Cow (retired look)"));
            Assert.That(retired.ContainsKey("female"), Is.False, "the original's sprites, through the template");
            Assert.That(session.WrittenAnimalVariants.Single().Retired, Is.True);
        });
    }
}
