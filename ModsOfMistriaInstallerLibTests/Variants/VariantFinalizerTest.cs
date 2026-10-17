using Garethp.ModsOfMistriaInstallerLib.Variants;
using ModsOfMistriaInstallerLibTests.TestUtils;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp.PixelFormats;
using Tomlyn.Model;
using static ModsOfMistriaInstallerLibTests.Variants.VariantTestGame;
using Toml = Garethp.ModsOfMistriaInstallerLib.Utils.Toml;

namespace ModsOfMistriaInstallerLibTests.Variants;

// The finalizer runs on the rebuilt archive after every mod is merged. These tests stand a fake
// archive in for it (the files the installers would have left) and check what it adds.
[TestFixture]
public class VariantFinalizerTest
{
    private const string Stores = """
        [general]
        name = "General Store"

        [[general.categories]]
        icon = "spr_ui_store_category_icon_decor"
        constant_stock = [
            "flour",
            "test_lamp",
            { item = "test_lamp", requirements = { repaired_general_store = true }, include_recipe = true },
            "sugar",
        ]
        random_stock = [
            "test_lamp",
            { item = "test_lamp", requirements = { is_season = "fall" } },
        ]

        [general.categories.seasonal]
        spring = [
            "seed_turnip",
            { item = "test_lamp", requirements = { is_season = "spring" } },
        ]
        winter = ["seed_beet"]
        """;

    private const string Cow = """
        [core]
        name = "Cow"

        [variants.default]
        acquirable = true
        [variants.default.female]
        key = "basic"
        [variants.default.male]
        key = "basic"
        [variants.default.baby]
        key = "basic_baby"

        [variants.brown]
        name = "Brown Cow"
        tier = 1
        default_unlocked = true
        born_in = ["spring"]
        """;

    private PristineAssets _pristine = null!;
    private VariantPlan _plan = null!;
    private List<ItemVariant> _items = null!;
    private string _witchyLook = "";
    private string _pastelLook = "";

    [SetUp]
    public void PlanTheLamp()
    {
        _pristine = WithLamp().Build();
        var framework = Framework();
        var witchy = ReplacingMod("alice.witchy_lamps", "Witchy Lamps", new Rgba32(1, 2, 3, 255), LampSprite);
        var pastel = ReplacingMod("bob.pastel_lamps", "Pastel Lamps", new Rgba32(4, 5, 6, 255), LampSprite);
        _plan = VariantPlanner.Plan([framework, witchy, pastel], framework, _pristine, FamilyIndex.Build(_pristine),
            new VariantPreferences());
        _items = VariantContentPlanner.Plan(_plan, _pristine).Items;
        _witchyLook = VariantFramework.LookIdForMod("alice.witchy_lamps");
        _pastelLook = VariantFramework.LookIdForMod("bob.pastel_lamps");
    }

    [TearDown]
    public void DisposePristine() => _pristine.Dispose();

    /// <summary>The rebuilt archive as the installers would leave it: game data plus the sprites
    /// that actually landed (every look sprite included unless told otherwise).</summary>
    private Dictionary<string, string> Archive(bool lookSpritesLanded = true)
    {
        var files = new Dictionary<string, string>
        {
            ["assets/fiddle/object_prototypes/furniture.toml"] =
                "[test_lamp]\nsize = [2, 2]\n[test_lamp.south]\nsprite = \"" + LampSprite + "\"\n",
            ["assets/fiddle/items/furniture/test_set.toml"] =
                "[test_lamp]\nname = \"Test Lamp\"\nicon_sprite = \"" + LampIcon + "\"\nobject = \"test_lamp\"\n" +
                "recipe_key = \"test_lamp\"\ntags = [\"furniture\", \"test_decor\"]\n",
            ["assets/fiddle/stores.toml"] = Stores,
            ["assets/fiddle/ranching/animals/cow.toml"] = Cow,
            ["assets/data_files/animation/shadow_manifest.json"] = "{ \"" + LampSprite + "\": \"" + LampShadow + "\" }",
        };
        foreach (var sprite in new[] { LampSprite, LampShadow, LampIcon, $"{LampSprite}_vanilla" })
            files[$"assets/animations/{FurnitureFolder}/{sprite}.meta.toml"] = SpriteMeta(16, 16);
        if (lookSpritesLanded)
            files[$"assets/animations/{FurnitureFolder}/{LampSprite}_{_witchyLook}.meta.toml"] = SpriteMeta(16, 16);
        return files;
    }

    private VariantSession Session(VariantHistory? history = null, bool retireOnly = false) =>
        VariantSession.Create(retireOnly ? new VariantPlan() : _plan, null, history ?? new VariantHistory(), null,
            retireOnly ? [] : _items, [], retireOnly);

    private static TomlArray Array(TomlTable table, params string[] path)
    {
        object current = table;
        foreach (var key in path)
            current = current switch
            {
                TomlTable t => t[key],
                TomlTableArray ta => ta[int.Parse(key)],
                _ => throw new InvalidOperationException(key),
            };
        return (TomlArray)current;
    }

    private static List<string> Entries(TomlArray array) => array
        .Select(entry => entry switch
        {
            string s => s,
            TomlTable t => "{" + t["item"] + (t.ContainsKey("requirements") ? " +req" : "") +
                           (t.ContainsKey("include_recipe") ? " +recipe" : "") + "}",
            _ => "?",
        })
        .ToList();

    [Test]
    public void ShouldWriteAVariantItemAndItsObject()
    {
        var files = new MockFileModifier(Archive());
        var session = Session();

        VariantFinalizer.Apply(files, session, (_, _) => { });

        var variant = $"test_lamp_{_witchyLook}";
        var furniture = Toml.ParseToml(files.GetFile("assets/fiddle/object_prototypes/furniture.toml"));
        var objectClone = (TomlTable)furniture[variant];
        Assert.That(((TomlTable)objectClone["south"])["sprite"], Is.EqualTo($"{LampSprite}_{_witchyLook}"));
        Assert.That(((TomlTable)((TomlTable)furniture["test_lamp"])["south"])["sprite"], Is.EqualTo(LampSprite), "the original is untouched");

        var items = Toml.ParseToml(files.GetFile("assets/" + VariantFinalizer.VariantItemsFile));
        var item = (TomlTable)items[variant];
        Assert.Multiple(() =>
        {
            Assert.That(item["object"], Is.EqualTo(variant));
            Assert.That(item["name"], Is.EqualTo("Test Lamp (Witchy Lamps)"));
            Assert.That(item["recipe_key"], Is.EqualTo("test_lamp"), "shares the original's recipe");
            Assert.That(item["icon_sprite"], Is.EqualTo(LampIcon));
            Assert.That(session.WrittenItemVariants.Select(v => v.ItemId), Is.EqualTo(new[] { variant }));
        });
    }

    [Test]
    public void ShouldSellEachVariantRightAfterItsOriginal()
    {
        var files = new MockFileModifier(Archive());

        VariantFinalizer.Apply(files, Session(), (_, _) => { });

        var variant = $"test_lamp_{_witchyLook}";
        var stores = Toml.ParseToml(files.GetFile("assets/fiddle/stores.toml"));
        Assert.Multiple(() =>
        {
            Assert.That(Entries(Array(stores, "general", "categories", "0", "constant_stock")), Is.EqualTo(new[]
            {
                "flour", "test_lamp", variant,
                "{test_lamp +req +recipe}", $"{{{variant} +req}}",
                "sugar",
            }));
            Assert.That(Entries(Array(stores, "general", "categories", "0", "seasonal", "spring")), Is.EqualTo(new[]
            {
                "seed_turnip", "{test_lamp +req}", $"{{{variant} +req}}",
            }));
            // The rotation is left alone so variants never crowd out other stock.
            Assert.That(Entries(Array(stores, "general", "categories", "0", "random_stock")), Is.EqualTo(new[]
            {
                "test_lamp", "{test_lamp +req}",
            }));
            Assert.That(Entries(Array(stores, "general", "categories", "0", "seasonal", "winter")), Is.EqualTo(new[] { "seed_beet" }));
        });

        // Requirements are copied, not shared or dropped.
        var cloned = (TomlTable)Array(stores, "general", "categories", "0", "constant_stock")[4]!;
        Assert.That(((TomlTable)cloned["requirements"])["repaired_general_store"], Is.EqualTo(true));
    }

    [Test]
    public void ShouldLinkTheShadowOfEveryNewSprite()
    {
        var files = new MockFileModifier(Archive());

        VariantFinalizer.Apply(files, Session(), (_, _) => { });

        var manifest = JObject.Parse(files.GetFile("assets/data_files/animation/shadow_manifest.json"));
        Assert.That((string?)manifest[$"{LampSprite}_{_witchyLook}"], Is.EqualTo(LampShadow));
        Assert.That((string?)manifest[$"{LampSprite}_vanilla"], Is.EqualTo(LampShadow));
        Assert.That((string?)manifest[LampSprite], Is.EqualTo(LampShadow));
    }

    [Test]
    public void ShouldSkipAVariantWhoseSpriteDidNotLand()
    {
        // A mod skipped for a GML problem takes its look sprites with it; the engine resolves
        // fiddle sprite names strictly at boot, so the variant must not name a missing sprite.
        var files = new MockFileModifier(Archive(lookSpritesLanded: false));
        var session = Session();

        VariantFinalizer.Apply(files, session, (_, _) => { });

        Assert.That(session.WrittenItemVariants, Is.Empty);
        Assert.That(files.Exists("assets/" + VariantFinalizer.VariantItemsFile), Is.False);
        Assert.That(files.GetFile("assets/fiddle/stores.toml"), Is.EqualTo(Stores));
        Assert.That(session.Plan.Warnings, Has.Some.Contains("is not installed"));
        // Nor does the shadow manifest name it.
        Assert.That(files.GetFile("assets/data_files/animation/shadow_manifest.json"), Does.Not.Contain(_witchyLook));
    }

    [Test]
    public void ShouldFallBackToARetiredStandInWhenAKnownVariantsArtDidNotLand()
    {
        // An earlier install generated this item, so saves may hold it. Its mod was skipped this
        // time (its look sprite did not land): the id must survive, with the original's art.
        var variant = $"test_lamp_{_witchyLook}";
        var history = new VariantHistory();
        history.Remember(_items, []);
        var files = new MockFileModifier(Archive(lookSpritesLanded: false));
        var session = Session(history);

        VariantFinalizer.Apply(files, session, (_, _) => { });

        var furniture = Toml.ParseToml(files.GetFile("assets/fiddle/object_prototypes/furniture.toml"));
        var items = Toml.ParseToml(files.GetFile("assets/" + VariantFinalizer.VariantItemsFile));
        Assert.Multiple(() =>
        {
            Assert.That(((TomlTable)((TomlTable)furniture[variant])["south"])["sprite"], Is.EqualTo(LampSprite));
            Assert.That(((TomlTable)items[variant])["name"], Is.EqualTo("Test Lamp (retired look)"));
            Assert.That(files.GetFile("assets/fiddle/stores.toml"), Is.EqualTo(Stores), "not sold while retired");
            Assert.That(session.WrittenItemVariants.Single().Retired, Is.True);
            Assert.That(session.Plan.Warnings, Has.Some.Contains("is not installed"));
        });
    }

    [Test]
    public void ShouldKeepARetiredItemLoadableButOutOfTheShops()
    {
        var history = new VariantHistory();
        history.Remember([new ItemVariant("furniture:test_lamp", "lkgone1234", "test_lamp", "test_lamp",
            "test_lamp_lkgone1234", "test_lamp_lkgone1234")], []);
        var files = new MockFileModifier(Archive());
        var session = Session(history);

        VariantFinalizer.Apply(files, session, (_, _) => { });

        var furniture = Toml.ParseToml(files.GetFile("assets/fiddle/object_prototypes/furniture.toml"));
        var retiredObject = (TomlTable)furniture["test_lamp_lkgone1234"];
        var items = Toml.ParseToml(files.GetFile("assets/" + VariantFinalizer.VariantItemsFile));
        var retired = (TomlTable)items["test_lamp_lkgone1234"];

        Assert.Multiple(() =>
        {
            // Same id, the original's art.
            Assert.That(((TomlTable)retiredObject["south"])["sprite"], Is.EqualTo(LampSprite));
            Assert.That(retired["object"], Is.EqualTo("test_lamp_lkgone1234"));
            Assert.That(retired["name"], Is.EqualTo("Test Lamp (retired look)"));
            Assert.That(retired["recipe_key"], Is.EqualTo("mvf_retired_test_lamp_lkgone1234"));
            Assert.That(((TomlArray)retired["tags"]).Cast<string>(), Is.EqualTo(new[] { "furniture" }));
            Assert.That(files.GetFile("assets/fiddle/stores.toml"), Does.Not.Contain("lkgone1234"));
            // The live variant is still generated and sold beside it.
            Assert.That(items.ContainsKey($"test_lamp_{_witchyLook}"), Is.True);
            // Only live variants go back into the history.
            Assert.That(session.WrittenItemVariants.Count(v => v.Retired), Is.EqualTo(1));
        });
    }

    [Test]
    public void ShouldWarnWhenARetiredItemsOriginalIsGone()
    {
        var history = new VariantHistory();
        history.Remember([new ItemVariant("furniture:gone", "lkgone1234", "gone", "gone", "gone_lkgone1234", "gone_lkgone1234")], []);
        var files = new MockFileModifier(Archive());
        var session = Session(history, retireOnly: true);

        Assert.DoesNotThrow(() => VariantFinalizer.Apply(files, session, (_, _) => { }));

        Assert.That(session.Plan.Warnings.Single(), Does.Contain("'gone_lkgone1234' could not be kept loadable"));
        Assert.That(files.Exists("assets/" + VariantFinalizer.VariantItemsFile), Is.False);
        Assert.That(Toml.ParseToml(files.GetFile("assets/fiddle/object_prototypes/furniture.toml")).ContainsKey("gone_lkgone1234"), Is.False);
    }

    [Test]
    public void ShouldKeepARetiredAnimalVariantLoadableButNotAdoptable()
    {
        var history = new VariantHistory();
        history.Remember([], [
            new AnimalVariant("animal:cow", "lkgone1234", "cow", "brown", "brown_lkgone1234"),
            new AnimalVariant("animal:cow", "lkgone1234", "cow", "spotted", "spotted_lkgone1234"),
            new AnimalVariant("animal:yak", "lkgone1234", "yak", "brown", "brown_lkgone1234"),
        ]);
        var files = new MockFileModifier(Archive());
        var session = Session(history, retireOnly: true);

        VariantFinalizer.Apply(files, session, (_, _) => { });

        var cow = Toml.ParseToml(files.GetFile("assets/fiddle/ranching/animals/cow.toml"));
        var variants = (TomlTable)cow["variants"];
        var retired = (TomlTable)variants["brown_lkgone1234"];
        Assert.Multiple(() =>
        {
            Assert.That(retired["acquirable"], Is.EqualTo(false));
            Assert.That(retired["name"], Is.EqualTo("Brown Cow (retired look)"));
            Assert.That(((TomlArray)retired["born_in"]), Is.Empty);
            Assert.That(retired.ContainsKey("default_unlocked"), Is.False);
            Assert.That(retired["tier"], Is.EqualTo(1L));
            Assert.That(variants.ContainsKey("brown"), Is.True);
            // The ones that cannot be rebuilt are reported, not silently dropped.
            Assert.That(session.Plan.Warnings, Has.Some.Contains("'spotted_lkgone1234'"));
            Assert.That(session.Plan.Warnings, Has.Some.Contains("the animal kind 'yak' is gone"));
        });
    }
}
