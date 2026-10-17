using Garethp.ModsOfMistriaInstallerLib.ModTypes;
using Garethp.ModsOfMistriaInstallerLib.Variants;
using ModsOfMistriaInstallerLibTests.Fixtures;

namespace ModsOfMistriaInstallerLibTests.ModTypes;

[TestFixture]
public class ModFileConflictDetectorTest
{
    [Test]
    public void FindsSharedDestinationPathAndIgnoresManifests()
    {
        var alpha = new MockMod(new Dictionary<string, object>
        {
            ["images/replace/shared.png"] = new byte[] { 1 },
            ["manifest.toml"] = "alpha"
        }) { Id = "alpha" };
        var beta = new MockMod(new Dictionary<string, object>
        {
            ["images/replace/shared.png"] = new byte[] { 2 },
            ["manifest.toml"] = "beta"
        }) { Id = "beta" };

        var conflicts = ModFileConflictDetector.Find([alpha, beta]);

        Assert.That(conflicts, Has.Count.EqualTo(1));
        Assert.That(conflicts[0].Path, Is.EqualTo("images/replace/shared.png"));
        Assert.That(conflicts[0].ModIds, Is.EquivalentTo(new[] { "alpha", "beta" }));
        Assert.That(conflicts[0].Kind, Is.EqualTo(ModFileConflictKind.HardReplacement));
    }

    [Test]
    public void ClassifiesMergeableMetadataAndSharedLocalization()
    {
        var alpha = new MockMod(new Dictionary<string, object>
        {
            ["animations/foo.meta.toml"] = "a",
            ["localization/l10n.meta.toml"] = "a"
        }) { Id = "alpha" };
        var beta = new MockMod(new Dictionary<string, object>
        {
            ["animations/foo.meta.toml"] = "b",
            ["localization/l10n.meta.toml"] = "b"
        }) { Id = "beta" };

        var conflicts = ModFileConflictDetector.Find([alpha, beta]);

        Assert.That(conflicts.Single(x => x.Path == "animations/foo.meta.toml").Kind,
            Is.EqualTo(ModFileConflictKind.MergeableMetadata));
        Assert.That(conflicts.Single(x => x.Path == "localization/l10n.meta.toml").Kind,
            Is.EqualTo(ModFileConflictKind.SharedLocalization));
    }

    [Test]
    public void IgnoresSharedLegacyCosmeticCategoryIcon()
    {
        var alpha = new MockMod(new Dictionary<string, object>
        {
            ["animations/UI NEW/Store Menu/spr_ui_store_category_icon_moddedcosmetic.png"] = new byte[] { 1 }
        }) { Id = "alpha" };
        var beta = new MockMod(new Dictionary<string, object>
        {
            ["animations/UI NEW/Store Menu/spr_ui_store_category_icon_moddedcosmetic.png"] = new byte[] { 2 }
        }) { Id = "beta" };

        Assert.That(ModFileConflictDetector.Find([alpha, beta]), Is.Empty);
    }

    [Test]
    public void FindsFontPathConflict()
    {
        var alpha = new MockMod(new Dictionary<string, object>
        {
            ["fonts/shared.ttf"] = new byte[] { 1 },
            ["fonts/shared.meta.toml"] = FontMetadata("font-a")
        }) { Id = "alpha" };
        var beta = new MockMod(new Dictionary<string, object>
        {
            ["fonts/shared.ttf"] = new byte[] { 2 },
            ["fonts/shared.meta.toml"] = FontMetadata("font-b")
        }) { Id = "beta" };

        var conflict = ModFileConflictDetector.Find([alpha, beta])
            .Single(item => item.Path == "fonts/shared.ttf");

        Assert.That(conflict.Kind, Is.EqualTo(ModFileConflictKind.FontAsset));
    }

    [Test]
    public void FindsFontIdConflictWhenSourcePathsDiffer()
    {
        var alpha = new MockMod(new Dictionary<string, object>
        {
            ["fonts/alpha.ttf"] = new byte[] { 1 },
            ["fonts/alpha.meta.toml"] = FontMetadata("shared-font")
        }) { Id = "alpha" };
        var beta = new MockMod(new Dictionary<string, object>
        {
            ["fonts/beta.ttf"] = new byte[] { 2 },
            ["fonts/beta.meta.toml"] = FontMetadata("shared-font")
        }) { Id = "beta" };

        var conflict = ModFileConflictDetector.Find([alpha, beta])
            .Single(item => item.Path == "fonts/@id/shared-font");

        Assert.That(conflict.Kind, Is.EqualTo(ModFileConflictKind.FontAsset));
        Assert.That(conflict.ModIds, Is.EquivalentTo(new[] { "alpha", "beta" }));
    }

    // ── Variants Framework ───────────────────────────────────────────────────────

    private const string Folder = "animations/Placeables/Furniture/Cottage Garden";
    private const string Lamp = "spr_decor_cottage_garden_lamp_south";

    // Two recolour mods that collide on everything the framework can turn into looks, plus the
    // things it cannot: a LUT palette, a fiddle table and a sprite neither of them replaces.
    private static (MockMod Alpha, MockMod Beta) CompetingRecolours() => (Recolour("alpha", 1), Recolour("beta", 2));

    private static MockMod Recolour(string id, byte colour) => new(new Dictionary<string, object>
    {
        ["images/replace/spr_ui_item_lamp.png"] = new byte[] { colour },
        ["images/replace/spr_ui_item_lamp.meta.toml"] = "meta",
        ["images/replace/spr_player_hair_lut.png"] = new byte[] { colour },
        ["images/replace/nested/spr_ui_item_lamp.png"] = new byte[] { colour },
        [$"{Folder}/{Lamp}.png"] = new byte[] { colour },
        [$"{Folder}/{Lamp}.meta.toml"] = "meta",
        ["shapes/Placeables/Furniture/Cottage Garden/poly_decor_cottage_garden_lamp_south.meta.toml"] = "shape",
        ["animations/Mod/spr_mod_new_thing.png"] = new byte[] { colour },
        ["animations/Mod/spr_mod_new_thing.meta.toml"] = "meta",
        ["fiddle/items/furniture/lamps.toml"] = "[lamp]",
    }) { Id = id };

    private static MockMod VariantsFramework() =>
        new(new Dictionary<string, object> { [VariantFramework.MarkerFile] = "" }) { Id = "mistria.variants" };

    private static Dictionary<string, ModFileConflictKind> Kinds(IReadOnlyList<ModFileConflict> conflicts) =>
        conflicts.ToDictionary(conflict => conflict.Path, conflict => conflict.Kind);

    [Test]
    public void ShouldReportSpriteOverlapsTheFrameworkResolvesAsHandled()
    {
        var (alpha, beta) = CompetingRecolours();
        var vanilla = new HashSet<string> { "spr_ui_item_lamp", Lamp, "spr_player_hair_lut" };

        var kinds = Kinds(ModFileConflictDetector.Find([VariantsFramework(), alpha, beta], vanilla.Contains));

        Assert.Multiple(() =>
        {
            Assert.That(kinds["images/replace/spr_ui_item_lamp.png"], Is.EqualTo(ModFileConflictKind.HandledByVariants));
            Assert.That(kinds["images/replace/spr_ui_item_lamp.meta.toml"], Is.EqualTo(ModFileConflictKind.HandledByVariants));
            Assert.That(kinds[$"{Folder}/{Lamp}.png"], Is.EqualTo(ModFileConflictKind.HandledByVariants));
            Assert.That(kinds[$"{Folder}/{Lamp}.meta.toml"], Is.EqualTo(ModFileConflictKind.HandledByVariants));
            Assert.That(kinds["shapes/Placeables/Furniture/Cottage Garden/poly_decor_cottage_garden_lamp_south.meta.toml"],
                Is.EqualTo(ModFileConflictKind.HandledByVariants));

            // LUT palettes stay on the installer's merge path.
            Assert.That(kinds["images/replace/spr_player_hair_lut.png"], Is.EqualTo(ModFileConflictKind.HardReplacement));
            // Never installed (images/replace is not recursive), so not the framework's either.
            Assert.That(kinds["images/replace/nested/spr_ui_item_lamp.png"], Is.EqualTo(ModFileConflictKind.HardReplacement));
            // New art two mods both add still overwrites; the framework only handles the game's art.
            Assert.That(kinds["animations/Mod/spr_mod_new_thing.png"], Is.EqualTo(ModFileConflictKind.SharedDestination));
            Assert.That(kinds["animations/Mod/spr_mod_new_thing.meta.toml"], Is.EqualTo(ModFileConflictKind.MergeableMetadata));
            Assert.That(kinds["fiddle/items/furniture/lamps.toml"], Is.EqualTo(ModFileConflictKind.MergeableMetadata));
        });
    }

    [Test]
    public void ShouldClassifyExactlyAsBeforeWithoutTheFramework()
    {
        var (alpha, beta) = CompetingRecolours();

        var without = ModFileConflictDetector.Find([alpha, beta]);
        var withPredicate = ModFileConflictDetector.Find([alpha, beta], _ => true);

        Assert.That(without.Select(c => c.Kind), Has.None.EqualTo(ModFileConflictKind.HandledByVariants));
        Assert.That(withPredicate, Is.EqualTo(without).Using<ModFileConflict>((a, b) =>
            a.Path == b.Path && a.Kind == b.Kind && a.ModIds.SequenceEqual(b.ModIds)));
        Assert.That(Kinds(without)["images/replace/spr_ui_item_lamp.png"], Is.EqualTo(ModFileConflictKind.HardReplacement));
        Assert.That(Kinds(without)[$"{Folder}/{Lamp}.png"], Is.EqualTo(ModFileConflictKind.SharedDestination));
    }

    [Test]
    public void ShouldTreatEverySharedSpriteAsTheGamesWhenTheGameIsNotAvailable()
    {
        var (alpha, beta) = CompetingRecolours();

        var kinds = Kinds(ModFileConflictDetector.Find([VariantsFramework(), alpha, beta]));

        Assert.That(kinds["animations/Mod/spr_mod_new_thing.png"], Is.EqualTo(ModFileConflictKind.HandledByVariants));
        Assert.That(kinds["images/replace/spr_player_hair_lut.png"], Is.EqualTo(ModFileConflictKind.HardReplacement));
    }

    [Test]
    public void ShouldLeaveAFullPathSpriteWithoutMetadataAlone()
    {
        // Without its .meta.toml the installer does not pack a full-path image, and the
        // framework does not turn it into a look either.
        var alpha = new MockMod(new Dictionary<string, object> { [$"{Folder}/{Lamp}.png"] = new byte[] { 1 } }) { Id = "alpha" };
        var beta = new MockMod(new Dictionary<string, object> { [$"{Folder}/{Lamp}.png"] = new byte[] { 2 } }) { Id = "beta" };

        var conflict = ModFileConflictDetector.Find([VariantsFramework(), alpha, beta]).Single();

        Assert.That(conflict.Kind, Is.EqualTo(ModFileConflictKind.SharedDestination));
    }

    // ── Portrait Wardrobe ────────────────────────────────────────────────────────

    private static MockMod PortraitWardrobeMod() =>
        new(new Dictionary<string, object> { ["fiddle/mods/portrait_wardrobe/registry.toml"] = "format = 1" })
            { Id = "silvertonguered.portrait_wardrobe" };

    private static MockMod PortraitPack(string id, byte colour) => new(new Dictionary<string, object>
    {
        ["images/replace/spr_portrait_march_beach_neutral.png"] = new byte[] { colour },
        ["images/replace/spr_portrait_effects_sweat.png"] = new byte[] { colour },
        ["images/replace/spr_ui_item_lamp.png"] = new byte[] { colour },
        ["animations/Mod/spr_portrait_march_bath_happy.png"] = new byte[] { colour },
        ["animations/Mod/spr_portrait_march_bath_happy.meta.toml"] = "meta",
    }) { Id = id };

    [Test]
    public void ShouldReportSharedPortraitsAsHandledWhenPortraitWardrobeIsSelected()
    {
        var vanilla = new HashSet<string> { "spr_portrait_march_beach_neutral", "spr_portrait_effects_sweat", "spr_ui_item_lamp" };

        var kinds = Kinds(ModFileConflictDetector.Find(
            [PortraitWardrobeMod(), PortraitPack("alpha", 1), PortraitPack("beta", 2)], vanilla.Contains));

        Assert.Multiple(() =>
        {
            Assert.That(kinds["images/replace/spr_portrait_march_beach_neutral.png"], Is.EqualTo(ModFileConflictKind.HandledByVariants));
            // a portrait only a mod adds (its own outfit) is kept side by side too
            Assert.That(kinds["animations/Mod/spr_portrait_march_bath_happy.png"], Is.EqualTo(ModFileConflictKind.HandledByVariants));
            Assert.That(kinds["animations/Mod/spr_portrait_march_bath_happy.meta.toml"], Is.EqualTo(ModFileConflictKind.HandledByVariants));
            // without the Variants Framework nothing else is handled: effect overlays and other art still overwrite
            Assert.That(kinds["images/replace/spr_portrait_effects_sweat.png"], Is.EqualTo(ModFileConflictKind.HardReplacement));
            Assert.That(kinds["images/replace/spr_ui_item_lamp.png"], Is.EqualTo(ModFileConflictKind.HardReplacement));
        });
    }

    [Test]
    public void ShouldLetTheVariantsFrameworkKeepPortraitEffectsWhenBothAreSelected()
    {
        var vanilla = new HashSet<string> { "spr_portrait_march_beach_neutral", "spr_portrait_effects_sweat", "spr_ui_item_lamp" };

        var kinds = Kinds(ModFileConflictDetector.Find(
            [VariantsFramework(), PortraitWardrobeMod(), PortraitPack("alpha", 1), PortraitPack("beta", 2)], vanilla.Contains));

        Assert.Multiple(() =>
        {
            Assert.That(kinds["images/replace/spr_portrait_march_beach_neutral.png"], Is.EqualTo(ModFileConflictKind.HandledByVariants));
            Assert.That(kinds["images/replace/spr_portrait_effects_sweat.png"], Is.EqualTo(ModFileConflictKind.HandledByVariants));
            Assert.That(kinds["images/replace/spr_ui_item_lamp.png"], Is.EqualTo(ModFileConflictKind.HandledByVariants));
            Assert.That(VariantFramework.IsPortraitSprite("spr_portrait_effects_sweat"), Is.False);
            Assert.That(VariantFramework.IsPortraitSprite("spr_portrait_march_beach_neutral"), Is.True);
        });
    }

    private static string FontMetadata(string id) =>
        $"[meta_properties]\nasset_kind = \"Font\"\nid = \"{id}\"\n";
}
