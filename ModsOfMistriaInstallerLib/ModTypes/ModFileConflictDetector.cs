using Garethp.ModsOfMistriaInstallerLib.PortraitWardrobe;
using Garethp.ModsOfMistriaInstallerLib.Utils;
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
    /// A portrait two or more mods replace while Portrait Wardrobe is selected. Wardrobe keeps every
    /// pack's version side by side (and the game's original) to pick in game, so load order no
    /// longer picks a winner and there is nothing for the user to do. Informational only.
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
    public static IReadOnlyList<ModFileConflict> Find(IEnumerable<IMod> mods)
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

        // Without Portrait Wardrobe every overlap keeps exactly the classification it always had.
        return PortraitWardrobeBuilder.IsFrameworkInstalled(selectedMods)
            ? ClassifyForPortraitWardrobe(ordered, selectedMods)
            : ordered;
    }

    // ── Portrait Wardrobe ────────────────────────────────────────────────────────

    /// <summary>
    /// Marks the portrait overlaps Portrait Wardrobe resolves at install time: a shared
    /// <c>images/replace/&lt;portrait&gt;.png</c>, a shared full-path <c>animations/**/spr_*.png</c>
    /// portrait (whether the game has it or a pack adds it), and the metadata that travels with
    /// them. LUT palettes and every other kind of art keep their old kind.
    /// </summary>
    private static List<ModFileConflict> ClassifyForPortraitWardrobe(
        List<ModFileConflict> conflicts, List<IMod> selectedMods)
    {
        var byId = selectedMods
            .GroupBy(mod => mod.GetId(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var images = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var conflict in conflicts)
            if (IsPortraitImage(conflict, byId))
                images.Add(conflict.Path.Replace('\\', '/'));

        if (images.Count == 0) return conflicts;

        return conflicts
            .Select(conflict => images.Contains(conflict.Path.Replace('\\', '/')) ||
                                PortraitImageFor(conflict.Path) is { } image && images.Contains(image)
                ? conflict with { Kind = ModFileConflictKind.HandledByVariants }
                : conflict)
            .ToList();
    }

    private static bool IsPortraitImage(ModFileConflict conflict, Dictionary<string, List<IMod>> byId)
    {
        var path = conflict.Path.Replace('\\', '/');
        if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return false;
        var sprite = Path.GetFileNameWithoutExtension(path);
        if (sprite.Contains("_lut", StringComparison.OrdinalIgnoreCase)) return false;
        if (!PortraitSpriteNames.IsPortrait(sprite)) return false;

        const string replaceFolder = "images/replace/";
        if (path.StartsWith(replaceFolder, StringComparison.OrdinalIgnoreCase))
        {
            // images/replace is read non-recursively by the installer; a nested file is never
            // installed, so it is not Wardrobe's to claim either.
            return !path[replaceFolder.Length..].Contains('/');
        }

        if (!path.StartsWith("animations/", StringComparison.OrdinalIgnoreCase)) return false;
        if (!sprite.StartsWith("spr_", StringComparison.Ordinal)) return false;

        // A full-path sprite is only installed (and only kept) with its own metadata.
        var meta = path[..^".png".Length] + ".meta.toml";
        return conflict.ModIds.All(id =>
            byId.TryGetValue(id, out var owners) && owners.Any(owner => SafeFileExists(owner, meta)));
    }

    /// <summary>The portrait image a metadata file belongs to, if it is one.</summary>
    private static string? PortraitImageFor(string path)
    {
        path = path.Replace('\\', '/');
        const string meta = ".meta.toml";
        if (!path.EndsWith(meta, StringComparison.OrdinalIgnoreCase)) return null;

        return path.StartsWith("images/replace/", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("animations/", StringComparison.OrdinalIgnoreCase)
            ? path[..^meta.Length] + ".png"
            : null;
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
