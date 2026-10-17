using Garethp.ModsOfMistriaInstallerLib.Generator;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;

namespace Garethp.ModsOfMistriaInstallerLib.Variants;

public enum ReplacementKind
{
    /// <summary><c>images/replace/&lt;sprite&gt;.png</c> (+ optional meta override).</summary>
    ImagesReplace,
    /// <summary>A PNG with its own <c>.meta.toml</c> at a path under <c>animations/</c>
    /// whose file name is a vanilla sprite's name.</summary>
    FullPath
}

/// <summary>One mod's replacement of one vanilla sprite, as mod-relative paths.</summary>
public sealed record Replacement(
    string Sprite,
    ReplacementKind Kind,
    string PngPath,
    string? MetaPath,
    string? ShapePath);

/// <summary>
/// Finds which vanilla sprites a mod replaces, reading the mod exactly the way the installers
/// do: <c>images/replace/</c> is read non-recursively (as <c>ImageInstaller</c> does), and a
/// full-path animation counts only when it ships the metadata that makes the installer pack it.
/// LUT palettes are left out on purpose: the installer merges competing palettes column by
/// column instead of letting one win, so they are never a look.
/// </summary>
public static class ReplacementScanner
{
    public static Dictionary<string, Replacement> Scan(IMod mod, PristineAssets pristine)
    {
        var found = new Dictionary<string, Replacement>(StringComparer.Ordinal);

        if (mod.HasFilesInFolder("images/replace", ".png"))
        {
            foreach (var entry in mod.GetFilesInFolder("images/replace", ".png"))
            {
                var rel = OutfitGenerator.Relativize(mod, entry).Replace('\\', '/');
                var sprite = Path.GetFileNameWithoutExtension(rel);
                if (!pristine.HasSprite(sprite) || FamilyIndex.IsLut(sprite)) continue;
                var meta = $"images/replace/{sprite}.meta.toml";
                found[sprite] = new Replacement(sprite, ReplacementKind.ImagesReplace, rel,
                    mod.FileExists(meta) ? meta : null, null);
            }
        }

        foreach (var entry in mod.GetAllFiles(".png"))
        {
            var rel = OutfitGenerator.Relativize(mod, entry).Replace('\\', '/');
            if (!rel.StartsWith("animations/", StringComparison.OrdinalIgnoreCase)) continue;
            var sprite = Path.GetFileNameWithoutExtension(rel);
            if (!sprite.StartsWith("spr_", StringComparison.Ordinal)) continue;
            if (!pristine.HasSprite(sprite) || FamilyIndex.IsLut(sprite)) continue;
            if (found.ContainsKey(sprite)) continue; // images/replace wins inside one mod, as installed

            var meta = rel[..^".png".Length] + ".meta.toml";
            if (!mod.FileExists(meta)) continue;

            var shapeRel = "shapes/" + rel["animations/".Length..];
            shapeRel = shapeRel[..(shapeRel.LastIndexOf('/') + 1)] + "poly_" + sprite["spr_".Length..] + ".meta.toml";
            found[sprite] = new Replacement(sprite, ReplacementKind.FullPath, rel, meta,
                mod.FileExists(shapeRel) ? shapeRel : null);
        }

        return found;
    }
}
