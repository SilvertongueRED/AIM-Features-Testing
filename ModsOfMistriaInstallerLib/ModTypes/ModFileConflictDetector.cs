using Garethp.ModsOfMistriaInstallerLib.Utils;
using Garethp.ModsOfMistriaInstallerLib.Variants;
using Tomlyn.Model;

namespace Garethp.ModsOfMistriaInstallerLib.ModTypes;

public enum ModFileConflictKind
{
    HardReplacement,
    FontAsset,
    MergeableMetadata,
    SharedLocalization,
    SharedDestination,

    /// <summary>
    /// A game sprite two or more mods replace while the Variants Framework mod is selected. The
    /// framework keeps every mod's version as a switchable look (or, for portraits and interface
    /// art, an in-game choice for the next install), so load order no longer picks a winner and
    /// there is nothing for the user to do. Informational only.
    /// </summary>
    HandledByVariants
}

public sealed record ModFileConflict(
    string Path,
    IReadOnlyList<string> ModIds,
    ModFileConflictKind Kind);

/// <summary>
/// Finds destination-path collisions between selected mods. It is deliberately
/// a read-only check and works for folders, ZIPs and RARs through IMod.
/// </summary>
public static class ModFileConflictDetector
{
    /// <param name="mods">The selected mods.</param>
    /// <param name="isVanillaSprite">
    /// Whether a sprite name belongs to the game itself. Only consulted when the Variants
    /// Framework mod is selected, to tell a shared replacement of the game's art (which the
    /// framework turns into looks) from a new sprite two mods both add (which still overwrite each
    /// other). Null when the game's archive is not available: every shared <c>spr_</c> image is
    /// then treated as a replacement, which is what nearly all of them are.
    /// </param>
    public static IReadOnlyList<ModFileConflict> Find(IEnumerable<IMod> mods, Func<string, bool>? isVanillaSprite = null)
    {
        var selectedMods = mods.ToList();
        var paths = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in selectedMods)
        {
            foreach (var file in mod.GetAllFiles(""))
            {
                var relative = RelativePath(mod, file);
                if (relative.Length == 0 || IsIgnorableDocumentation(relative) || IsManifest(relative) ||
                    IsSharedGeneratedCosmeticCategoryIcon(relative)) continue;
                if (!paths.TryGetValue(relative, out var owners))
                    paths[relative] = owners = new(StringComparer.OrdinalIgnoreCase);
                owners.Add(mod.GetId());
            }
        }

        var conflicts = paths
            .Where(entry => entry.Value.Count > 1)
            .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new ModFileConflict(
                entry.Key,
                entry.Value.Order(StringComparer.OrdinalIgnoreCase).ToList(),
                Classify(entry.Key)))
            .ToList();

        // Font metadata carries the engine identity. Two different source
        // filenames can still describe the same runtime font, so destination
        // path checks alone would miss this collision.
        var fontIds = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in selectedMods)
        {
            foreach (var file in mod.GetAllFiles(".meta.toml"))
            {
                var relative = RelativePath(mod, file);
                if (!IsFontMetadata(relative)) continue;

                string? fontId;
                try
                {
                    var table = Toml.ParseToml(mod.ReadFile(file));
                    fontId = ReadFontId(table);
                }
                catch
                {
                    // TOML validation reports malformed metadata separately.
                    continue;
                }

                if (string.IsNullOrWhiteSpace(fontId)) continue;
                if (!fontIds.TryGetValue(fontId, out var owners))
                    fontIds[fontId] = owners = new(StringComparer.OrdinalIgnoreCase);
                owners.Add(mod.GetId());
            }
        }

        foreach (var (fontId, owners) in fontIds.Where(entry => entry.Value.Count > 1))
        {
            var key = $"fonts/@id/{fontId}";
            if (conflicts.Any(conflict =>
                    conflict.Kind == ModFileConflictKind.FontAsset &&
                    conflict.Path.Equals(key, StringComparison.OrdinalIgnoreCase)))
                continue;

            conflicts.Add(new ModFileConflict(
                key,
                owners.Order(StringComparer.OrdinalIgnoreCase).ToList(),
                ModFileConflictKind.FontAsset));
        }

        var ordered = conflicts
            .OrderBy(conflict => conflict.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Without either framework every overlap keeps exactly the classification it always had.
        // Portrait Wardrobe alone keeps every pack's portraits side by side (to pick in game), so
        // shared portraits are handled then too - and only those.
        var variants = selectedMods.Any(VariantFramework.IsFrameworkMod);
        var portraitWardrobe = PortraitWardrobe.PortraitWardrobeBuilder.IsFrameworkInstalled(selectedMods);
        if (!variants && !portraitWardrobe) return ordered;
        return ClassifyForVariants(ordered, selectedMods, isVanillaSprite, variants, portraitWardrobe);
    }

    // ── Variants Framework ───────────────────────────────────────────────────────

    /// <summary>
    /// Marks the overlaps the Variants Framework resolves at install time: a shared
    /// <c>images/replace/&lt;sprite&gt;.png</c>, a shared full-path <c>animations/**/spr_*.png</c>
    /// that replaces a game sprite, and the metadata and collision shape that travel with them.
    /// These are exactly the replacements <see cref="ReplacementScanner"/> hands the planner, so
    /// the report never calls something a conflict that the install will turn into looks. LUT
    /// palettes keep their old kind: the installer merges those column by column instead.
    /// </summary>
    private static List<ModFileConflict> ClassifyForVariants(
        List<ModFileConflict> conflicts, List<IMod> selectedMods, Func<string, bool>? isVanillaSprite,
        bool variants = true, bool portraitWardrobe = false)
    {
        var byId = selectedMods
            .GroupBy(mod => mod.GetId(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var images = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var conflict in conflicts)
            if (IsLookImage(conflict, byId, isVanillaSprite, variants, portraitWardrobe))
                images.Add(conflict.Path.Replace('\\', '/'));

        if (images.Count == 0) return conflicts;

        return conflicts
            .Select(conflict => images.Contains(conflict.Path.Replace('\\', '/')) ||
                                LookImageFor(conflict.Path) is { } image && images.Contains(image)
                ? conflict with { Kind = ModFileConflictKind.HandledByVariants }
                : conflict)
            .ToList();
    }

    private static bool IsLookImage(ModFileConflict conflict, Dictionary<string, List<IMod>> byId,
        Func<string, bool>? isVanillaSprite, bool variants = true, bool portraitWardrobe = false)
    {
        var path = conflict.Path.Replace('\\', '/');
        if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return false;
        var sprite = Path.GetFileNameWithoutExtension(path);
        if (sprite.Contains("_lut", StringComparison.OrdinalIgnoreCase)) return false;

        // Portrait Wardrobe owns portraits when it is selected, and keeps every pack's version
        // whether the game has that portrait or a pack adds it (an outfit only a mod has).
        var portrait = PortraitWardrobe.PortraitSpriteNames.IsPortrait(sprite);
        if (portraitWardrobe && portrait) isVanillaSprite = null;
        else if (!variants) return false;

        const string replaceFolder = "images/replace/";
        if (path.StartsWith(replaceFolder, StringComparison.OrdinalIgnoreCase))
        {
            // images/replace is read non-recursively by the installer; a nested file is never
            // installed, so it is not the framework's to claim either.
            if (path[replaceFolder.Length..].Contains('/')) return false;
            return isVanillaSprite?.Invoke(sprite) ?? true;
        }

        if (!path.StartsWith("animations/", StringComparison.OrdinalIgnoreCase)) return false;
        if (!sprite.StartsWith("spr_", StringComparison.Ordinal)) return false;
        if (!(isVanillaSprite?.Invoke(sprite) ?? true)) return false;

        // A full-path sprite is only installed (and only becomes a look) with its own metadata.
        var meta = path[..^".png".Length] + ".meta.toml";
        return conflict.ModIds.All(id =>
            byId.TryGetValue(id, out var owners) && owners.Any(owner => SafeFileExists(owner, meta)));
    }

    /// <summary>The look image a metadata or collision-shape file belongs to, if it is one.</summary>
    private static string? LookImageFor(string path)
    {
        path = path.Replace('\\', '/');
        const string meta = ".meta.toml";
        if (!path.EndsWith(meta, StringComparison.OrdinalIgnoreCase)) return null;

        if (path.StartsWith("images/replace/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("animations/", StringComparison.OrdinalIgnoreCase))
            return path[..^meta.Length] + ".png";

        // shapes/<folder>/poly_<name>.meta.toml goes with animations/<folder>/spr_<name>.png.
        if (!path.StartsWith("shapes/", StringComparison.OrdinalIgnoreCase)) return null;
        var slash = path.LastIndexOf('/');
        var file = path[(slash + 1)..^meta.Length];
        if (!file.StartsWith("poly_", StringComparison.Ordinal)) return null;
        var folder = path["shapes/".Length..(slash + 1)];
        return "animations/" + folder + "spr_" + file["poly_".Length..] + ".png";
    }

    private static bool SafeFileExists(IMod mod, string path)
    {
        try
        {
            return mod.FileExists(path);
        }
        catch
        {
            return false;
        }
    }

    private static ModFileConflictKind Classify(string path)
    {
        var normalized = path.Replace('\\', '/');

        if (normalized.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) &&
            normalized.EndsWith(".bank", StringComparison.OrdinalIgnoreCase))
            return ModFileConflictKind.HardReplacement;

        if (normalized.StartsWith("images/replace/", StringComparison.OrdinalIgnoreCase) ||
            (normalized.StartsWith("animations/ui new/loading screen/", StringComparison.OrdinalIgnoreCase) &&
             normalized.EndsWith(".png", StringComparison.OrdinalIgnoreCase)))
            return ModFileConflictKind.HardReplacement;

        if (IsFontPath(normalized))
            return ModFileConflictKind.FontAsset;

        if (normalized.Equals("localization/l10n.meta.toml", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("fiddle/ui/text_styles.toml", StringComparison.OrdinalIgnoreCase))
            return ModFileConflictKind.SharedLocalization;

        if (normalized.EndsWith(".meta.toml", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("fiddle/", StringComparison.OrdinalIgnoreCase))
            return ModFileConflictKind.MergeableMetadata;

        return ModFileConflictKind.SharedDestination;
    }

    private static bool IsFontPath(string path) =>
        path.StartsWith("fonts/", StringComparison.OrdinalIgnoreCase) &&
        (path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith(".meta.toml", StringComparison.OrdinalIgnoreCase));

    private static bool IsFontMetadata(string path) =>
        path.StartsWith("fonts/", StringComparison.OrdinalIgnoreCase) &&
        path.EndsWith(".meta.toml", StringComparison.OrdinalIgnoreCase);

    private static string? ReadFontId(TomlTable table)
    {
        if (!table.TryGetValue("meta_properties", out var metaObject) || metaObject is not TomlTable meta)
            return null;
        if (!meta.TryGetValue("asset_kind", out var kind) ||
            !string.Equals(kind as string, "Font", StringComparison.OrdinalIgnoreCase))
            return null;
        return meta.TryGetValue("id", out var id) ? id as string : null;
    }

    private static string RelativePath(IMod mod, string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        var basePath = mod.GetBasePath().Replace('\\', '/').TrimEnd('/');
        if (basePath.Length > 0)
        {
            if (normalized.Equals(basePath, StringComparison.OrdinalIgnoreCase)) return "";
            var prefix = basePath + "/";
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                normalized = normalized[prefix.Length..];
        }
        return normalized;
    }

    private static bool IsManifest(string path) =>
        path.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("manifest.toml", StringComparison.OrdinalIgnoreCase);

    // Legacy outfit mods share this category icon, but AIM registers each mod's
    // player assets and store entries independently. It is not a load-order
    // conflict and should not drown out genuine cosmetic compatibility issues.
    private static bool IsSharedGeneratedCosmeticCategoryIcon(string path) =>
        path.Replace('\\', '/').Equals(
            "animations/UI NEW/Store Menu/spr_ui_store_category_icon_moddedcosmetic.png",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsIgnorableDocumentation(string path)
    {
        var name = Path.GetFileName(path);
        return name.Equals("README.md", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("CHANGELOG.md", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("LICENSE", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("LICENSE.", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
    }
}
