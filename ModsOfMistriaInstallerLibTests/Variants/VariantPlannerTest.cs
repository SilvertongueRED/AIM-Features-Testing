using Garethp.ModsOfMistriaInstallerLib.ModTypes;
using Garethp.ModsOfMistriaInstallerLib.Variants;
using ModsOfMistriaInstallerLibTests.Fixtures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Tomlyn.Model;
using static ModsOfMistriaInstallerLibTests.Variants.VariantTestGame;
using Toml = Garethp.ModsOfMistriaInstallerLib.Utils.Toml;

namespace ModsOfMistriaInstallerLibTests.Variants;

// Two mods recolour the same lamp. Without the framework the later one simply wins; with it
// both survive as looks, the game's art is kept as the "Original" look, and the base sprite still
// shows exactly what it showed before (the later mod), so nothing changes for a player who never
// opens the picker.
[TestFixture]
public class VariantPlannerTest
{
    private static readonly Rgba32 Witchy = new(40, 0, 60, 255);
    private static readonly Rgba32 Pastel = new(255, 200, 220, 255);

    private PristineAssets _pristine = null!;
    private MockMod _framework = null!;
    private MockMod _witchy = null!;
    private MockMod _pastel = null!;
    private string _witchyLook = "";
    private string _pastelLook = "";

    [SetUp]
    public void BuildGame()
    {
        _pristine = WithLamp().Build();
        _framework = Framework();
        _witchy = ReplacingMod("alice.witchy_lamps", "Witchy Lamps", Witchy, LampSprite);
        _pastel = ReplacingMod("bob.pastel_lamps", "Pastel Lamps", Pastel, LampSprite);
        _witchyLook = VariantFramework.LookIdForMod(_witchy.GetId());
        _pastelLook = VariantFramework.LookIdForMod(_pastel.GetId());
    }

    [TearDown]
    public void DisposeGame() => _pristine.Dispose();

    private VariantPlan Plan(VariantPreferences? preferences = null, params IMod[] mods) =>
        VariantPlanner.Plan(mods, _framework, _pristine, FamilyIndex.Build(_pristine), preferences ?? new VariantPreferences());

    [Test]
    public void ShouldFindTheReplacementsAModMakes()
    {
        var found = ReplacementScanner.Scan(_witchy, _pristine);

        Assert.That(found.Keys, Is.EqualTo(new[] { LampSprite }));
        Assert.That(found[LampSprite].Kind, Is.EqualTo(ReplacementKind.ImagesReplace));
        Assert.That(found[LampSprite].PngPath, Is.EqualTo($"images/replace/{LampSprite}.png"));
    }

    [Test]
    public void ShouldTurnCompetingReplacementsIntoLooks()
    {
        var plan = Plan(null, _framework, _witchy, _pastel);

        var family = plan.RuntimeFamilies.Single();
        Assert.Multiple(() =>
        {
            Assert.That(family.Family.Id, Is.EqualTo("furniture:test_lamp"));
            Assert.That(family.Family.Kind, Is.EqualTo(FamilyKind.Furniture));
            Assert.That(family.Family.DisplayName, Is.EqualTo("Test Lamp"));
            Assert.That(family.Family.ItemIds, Is.EqualTo(new[] { "test_lamp" }));
            Assert.That(family.Looks.Select(l => l.Look.Id), Is.EqualTo(new[] { "vanilla", _witchyLook, _pastelLook }));
            Assert.That(plan.Looks[_witchyLook].Name, Is.EqualTo("Witchy Lamps"));
            Assert.That(plan.Looks[_witchyLook].ModId, Is.EqualTo("alice.witchy_lamps"));
            Assert.That(plan.InstallOnlyFamilies, Is.Empty);
        });
    }

    [Test]
    public void ShouldKeepTheLastModInLoadOrderAsTheDefault()
    {
        var plan = Plan(null, _framework, _witchy, _pastel);
        var family = plan.RuntimeFamilies.Single();

        Assert.That(family.InstallDefault, Is.EqualTo(_pastelLook));

        // The default look shows the base sprite, which the default mod still replaces.
        var pastel = family.LookById(_pastelLook)!;
        Assert.That(pastel.Map, Is.Empty);
        Assert.That(pastel.Changed, Is.EqualTo(new[] { LampSprite }));
        Assert.That(plan.Rewrites.ContainsKey(_pastel.GetId()), Is.False, "the default mod's files are untouched");

        // Reversing the load order reverses the default.
        var reversed = Plan(null, _framework, _pastel, _witchy).RuntimeFamilies.Single();
        Assert.That(reversed.InstallDefault, Is.EqualTo(_witchyLook));
    }

    [Test]
    public void ShouldPackTheLosersArtUnderItsOwnLookSprite()
    {
        var plan = Plan(null, _framework, _witchy, _pastel);
        var family = plan.RuntimeFamilies.Single();
        var lookSprite = $"{LampSprite}_{_witchyLook}";

        var witchy = family.LookById(_witchyLook)!;
        Assert.That(witchy.Map[LampSprite], Is.EqualTo(lookSprite));
        Assert.That(witchy.Changed, Is.EqualTo(new[] { LampSprite }));

        var rewrite = plan.Rewrites[_witchy.GetId()];
        var folder = $"animations/{FurnitureFolder}";
        Assert.Multiple(() =>
        {
            // Its replacement no longer lands on the shared sprite...
            Assert.That(rewrite.Hidden, Does.Contain($"images/replace/{LampSprite}.png"));
            // ...it is added as a sprite of its own, with metadata and the vanilla collision shape.
            Assert.That(rewrite.Added.Keys, Is.EquivalentTo(new[]
            {
                $"{folder}/{lookSprite}.png",
                $"{folder}/{lookSprite}.meta.toml",
                $"shapes/{FurnitureFolder}/poly_{lookSprite["spr_".Length..]}.meta.toml",
            }));
            Assert.That(plan.LookSpriteCount, Is.EqualTo(1));
        });

        // The look sprite is the mod's own image, byte for byte.
        var png = rewrite.Added[$"{folder}/{lookSprite}.png"].Bytes!();
        using (var image = Image.Load<Rgba32>(png))
            Assert.That(image[3, 3], Is.EqualTo(Witchy));

        // Metadata: the game's, with its identity removed so the installer allocates a new sprite.
        var meta = Toml.ParseToml(rewrite.Added[$"{folder}/{lookSprite}.meta.toml"].Text!);
        var metaProperties = (TomlTable)meta["meta_properties"];
        var assetProperties = (TomlTable)meta["asset_properties"];
        Assert.That(metaProperties.ContainsKey("id"), Is.False);
        Assert.That(metaProperties["asset_kind"], Is.EqualTo("Animation"));
        Assert.That(assetProperties["atlas"], Is.EqualTo("Default"));
        Assert.That(((TomlArray)assetProperties["frame_size"]).Select(Convert.ToInt64), Is.EqualTo(new long[] { 16, 16 }));

        var shape = Toml.ParseToml(rewrite.Added[$"shapes/{FurnitureFolder}/poly_{lookSprite["spr_".Length..]}.meta.toml"].Text!);
        var shapeMeta = (TomlTable)shape["meta_properties"];
        Assert.That(shapeMeta.ContainsKey("id") || shapeMeta.ContainsKey("required_assets"), Is.False);
    }

    [Test]
    public void ShouldKeepACopyOfTheGamesArtForTheOriginalLook()
    {
        var plan = Plan(null, _framework, _witchy, _pastel);
        var family = plan.RuntimeFamilies.Single();
        var copy = $"{LampSprite}_vanilla";

        Assert.That(family.LookById("vanilla")!.Map[LampSprite], Is.EqualTo(copy));
        Assert.That(plan.VanillaCopyCount, Is.EqualTo(1));

        var framework = plan.Rewrites[_framework.GetId()];
        var png = framework.Added[$"animations/{FurnitureFolder}/{copy}.png"].Bytes!();
        using var image = Image.Load<Rgba32>(png);
        Assert.That(image[3, 3], Is.EqualTo(new Rgba32(200, 200, 200, 255)));
        Assert.That(framework.Added.ContainsKey($"animations/{FurnitureFolder}/{copy}.meta.toml"), Is.True);
        Assert.That(framework.Added.ContainsKey($"shapes/{FurnitureFolder}/poly_{copy["spr_".Length..]}.meta.toml"), Is.True);
    }

    [Test]
    public void ShouldGiveNewLookSpritesTheVanillaShadow()
    {
        var plan = Plan(null, _framework, _witchy, _pastel);

        Assert.That(plan.ShadowLinks, Is.EqualTo(new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            [$"{LampSprite}_{_witchyLook}"] = LampShadow,
            [$"{LampSprite}_vanilla"] = LampShadow,
        }));
    }

    [Test]
    public void ShouldPreviewEachLookWithTheWorldSprite()
    {
        var family = Plan(null, _framework, _witchy, _pastel).RuntimeFamilies.Single();

        Assert.That(family.Family.PreviewSprite, Is.EqualTo(LampSprite));
        Assert.That(family.Looks.Select(l => l.PreviewSprite), Has.All.EqualTo(LampSprite));
        Assert.That(family.Looks.Select(l => l.Resolve(LampSprite)),
            Is.EqualTo(new[] { $"{LampSprite}_vanilla", $"{LampSprite}_{_witchyLook}", LampSprite }));
    }

    [Test]
    public void ShouldLetThePlayersChoiceOverrideLoadOrder()
    {
        var preferences = new VariantPreferences();
        preferences.GlobalLooks["furniture:test_lamp"] = _witchyLook;

        var plan = Plan(preferences, _framework, _witchy, _pastel);
        var family = plan.RuntimeFamilies.Single();

        Assert.Multiple(() =>
        {
            Assert.That(family.InstallDefault, Is.EqualTo(_witchyLook));
            Assert.That(family.LookById(_witchyLook)!.Map, Is.Empty);
            Assert.That(family.LookById(_pastelLook)!.Map[LampSprite], Is.EqualTo($"{LampSprite}_{_pastelLook}"));
            Assert.That(plan.Rewrites[_pastel.GetId()].Hidden, Does.Contain($"images/replace/{LampSprite}.png"));
            Assert.That(plan.Rewrites.ContainsKey(_witchy.GetId()), Is.False);
        });
    }

    [Test]
    public void ShouldLetThePlayerChooseTheOriginalAsDefault()
    {
        var preferences = new VariantPreferences();
        preferences.GlobalLooks["furniture:test_lamp"] = VariantFramework.VanillaLook;

        var plan = Plan(preferences, _framework, _witchy, _pastel);
        var family = plan.RuntimeFamilies.Single();

        Assert.Multiple(() =>
        {
            Assert.That(family.InstallDefault, Is.EqualTo("vanilla"));
            // Every mod's art moves to a look sprite, so the base sprite stays the game's own.
            Assert.That(plan.Rewrites[_witchy.GetId()].Hidden, Does.Contain($"images/replace/{LampSprite}.png"));
            Assert.That(plan.Rewrites[_pastel.GetId()].Hidden, Does.Contain($"images/replace/{LampSprite}.png"));
            Assert.That(family.LookById("vanilla")!.Map, Is.Empty);
            Assert.That(plan.VanillaCopyCount, Is.Zero);
        });
    }

    [Test]
    public void ShouldIgnoreAPreferenceForALookThatIsNotInstalled()
    {
        var preferences = new VariantPreferences();
        preferences.GlobalLooks["furniture:test_lamp"] = "lkgonemod1234";

        Assert.That(Plan(preferences, _framework, _witchy, _pastel).RuntimeFamilies.Single().InstallDefault,
            Is.EqualTo(_pastelLook));
    }

    [Test]
    public void ShouldKeepTheOriginalAsALookForASingleMod()
    {
        var plan = Plan(null, _framework, _witchy);

        var family = plan.RuntimeFamilies.Single();
        Assert.That(family.Looks.Select(l => l.Look.Id), Is.EqualTo(new[] { "vanilla", _witchyLook }));
        Assert.That(family.InstallDefault, Is.EqualTo(_witchyLook));

        var off = new VariantPreferences { KeepOriginalLooks = false };
        Assert.That(Plan(off, _framework, _witchy).RuntimeFamilies, Is.Empty);
        Assert.That(Plan(off, _framework, _witchy).Rewrites, Is.Empty);
    }

    [Test]
    public void ShouldLeaveAnImageThatDoesNotFitTheFrameLayoutAsAPlainReplacement()
    {
        // The game's lamp is a two-frame strip; a 15 pixel wide image cannot be cut into two
        // frames, so that mod's art cannot become a look of its own.
        var game = WithLamp();
        game.Text($"assets/animations/{FurnitureFolder}/{LampSprite}.meta.toml", SpriteMeta(8, 16, frames: 2));
        using var pristine = game.Build();
        var odd = new MockMod(new Dictionary<string, object>
        {
            [$"images/replace/{LampSprite}.png"] = Png(15, 16, Witchy),
        }) { Id = "alice.witchy_lamps", Name = "Witchy Lamps" };

        var plan = VariantPlanner.Plan([_framework, odd, _pastel], _framework, pristine,
            FamilyIndex.Build(pristine), new VariantPreferences());

        Assert.Multiple(() =>
        {
            Assert.That(plan.Rewrites.ContainsKey(odd.GetId()), Is.False, "the mod installs exactly as without the framework");
            Assert.That(plan.Warnings.Single(), Does.Contain("Witchy Lamps"));
            Assert.That(plan.RuntimeFamilies.Single().Looks.Select(l => l.Look.Id),
                Is.EqualTo(new[] { "vanilla", _pastelLook }));
        });
    }

    [Test]
    public void ShouldNotOfferAnOriginalLookItCannotShow()
    {
        // No image of the game's own lamp to copy: an "Original" look would show the default
        // mod's art (which the base sprite holds), so only the mods' looks are offered.
        var game = WithLamp();
        game.Files.Remove($"assets/animations/{FurnitureFolder}/{LampSprite}.png");
        using var pristine = game.Build();

        var plan = VariantPlanner.Plan([_framework, _witchy, _pastel], _framework, pristine,
            FamilyIndex.Build(pristine), new VariantPreferences());

        var family = plan.RuntimeFamilies.Single();
        Assert.That(family.Looks.Select(l => l.Look.Id), Is.EqualTo(new[] { _witchyLook, _pastelLook }));
        Assert.That(plan.VanillaCopyCount, Is.Zero);
        Assert.That(plan.Warnings, Has.Some.Contains("original art"));
    }

    [Test]
    public void ShouldOfferAnInstallTimeChoiceForArtTheEngineCannotSwap()
    {
        // A portrait belongs to no runtime family: one version is installed, the other is kept as
        // a thumbnail so the in-game catalog can offer it for the next install.
        var game = WithLamp();
        game.Sprite("Portraits/Test", "spr_portrait_test_neutral", 16, 16, new Rgba32(1, 1, 1, 255), atlas: "Portraits");
        using var pristine = game.Build();
        var witchy = ReplacingMod("alice.witchy_lamps", "Witchy Lamps", Witchy, "spr_portrait_test_neutral");
        var pastel = ReplacingMod("bob.pastel_lamps", "Pastel Lamps", Pastel, "spr_portrait_test_neutral");

        var plan = VariantPlanner.Plan([_framework, witchy, pastel], _framework, pristine,
            FamilyIndex.Build(pristine), new VariantPreferences());

        var choice = plan.InstallOnlyFamilies.Single();
        Assert.Multiple(() =>
        {
            Assert.That(plan.RuntimeFamilies, Is.Empty);
            Assert.That(choice.Family.Kind, Is.EqualTo(FamilyKind.InstallOnly));
            Assert.That(choice.Family.Id, Does.Match("^scene:[0-9a-z]{8}$"));
            Assert.That(choice.InstallDefault, Is.EqualTo(_pastelLook));
            Assert.That(plan.Rewrites[witchy.GetId()].Hidden, Does.Contain("images/replace/spr_portrait_test_neutral.png"));
            Assert.That(plan.Rewrites.ContainsKey(pastel.GetId()), Is.False);
            Assert.That(choice.Looks.Select(l => l.Thumbnail), Has.All.StartWith("spr_mvf_thumb_"));
            Assert.That(plan.Rewrites[_framework.GetId()].Added.Keys.Count(k => k.EndsWith(".png", StringComparison.Ordinal)),
                Is.EqualTo(3));
        });
    }

    [Test]
    public void ShouldPlanOneVariantItemPerNonDefaultLook()
    {
        var plan = Plan(null, _framework, _witchy, _pastel);

        var (items, animals) = VariantContentPlanner.Plan(plan, _pristine);

        Assert.That(animals, Is.Empty);
        Assert.That(items, Is.EqualTo(new[]
        {
            new ItemVariant("furniture:test_lamp", _witchyLook, "test_lamp", "test_lamp",
                $"test_lamp_{_witchyLook}", $"test_lamp_{_witchyLook}"),
        }));
        Assert.That(items.All(i => VariantFramework.IsCanonicalId(i.ItemId)), Is.True);

        var withOriginal = new VariantPreferences { VariantItemsOriginal = true };
        var (both, _) = VariantContentPlanner.Plan(Plan(withOriginal, _framework, _witchy, _pastel), _pristine);
        Assert.That(both.Select(i => i.LookId), Is.EquivalentTo(new[] { "vanilla", _witchyLook }));

        var none = new VariantPreferences { VariantItems = false };
        Assert.That(VariantContentPlanner.Plan(Plan(none, _framework, _witchy, _pastel), _pristine).Items, Is.Empty);
    }

    [Test]
    public void ShouldNotCloneObjectsTheEnginesCodeNamesDirectly()
    {
        var game = WithLamp();
        game.Text("assets/gml/scripts/Mailbox.gml", "if node.object_id == ObjectId.TestLamp { open_mail(); }\n");
        using var pristine = game.Build();
        var plan = VariantPlanner.Plan([_framework, _witchy, _pastel], _framework, pristine,
            FamilyIndex.Build(pristine), new VariantPreferences());

        Assert.That(plan.RuntimeFamilies, Has.Count.EqualTo(1), "the look itself is still offered");
        Assert.That(VariantContentPlanner.Plan(plan, pristine).Items, Is.Empty);
        Assert.That(VariantContentPlanner.PascalToSnake("CottageGardenArchV2"), Is.EqualTo("cottage_garden_arch_v2"));
    }
}
