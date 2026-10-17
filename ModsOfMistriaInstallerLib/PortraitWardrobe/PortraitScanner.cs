using Garethp.ModsOfMistriaInstallerLib.Collector;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;

namespace Garethp.ModsOfMistriaInstallerLib.PortraitWardrobe;

/// <summary>One portrait a mod supplies.</summary>
/// <param name="SpriteName">The game sprite name, e.g. <c>spr_portrait_march_beach_neutral</c>.</param>
/// <param name="PngPath">The PNG, as the mod's own file API names it (pass it back to that mod).</param>
/// <param name="MetaPath">The mod's .meta.toml for it, when it ships one.</param>
/// <param name="IsReplacement">True for <c>images/replace/</c>: the mod overwrites an existing game sprite.</param>
public sealed record PortraitContribution(string SpriteName, string PngPath, string? MetaPath, bool IsReplacement);

/// <summary>
/// Finds the portraits a mod ships, the same two ways the installer itself installs images:
/// <list type="bullet">
///   <item><c>images/replace/spr_portrait_*.png</c> - replaces a game portrait by name.</item>
///   <item>any <c>spr_portrait_*.meta.toml</c> with a PNG beside it, in any folder - a new portrait
///         (often for an outfit the same mod adds), or a same-named one that overrides the game's.</item>
/// </list>
/// Nothing about the mod needs to change for this: it is read exactly as AIM already reads it.
/// </summary>
public static class PortraitScanner
{
    public static List<PortraitContribution> Scan(IMod mod)
    {
        var found = new Dictionary<string, PortraitContribution>(StringComparer.OrdinalIgnoreCase);

        if (mod.HasFilesInFolder("images/replace", ".png"))
        {
            foreach (var png in mod.GetFilesInFolder("images/replace", ".png"))
            {
                var name = Path.GetFileNameWithoutExtension(png.Replace('\\', '/'));
                if (!PortraitSpriteNames.IsPortrait(name)) continue;
                var meta = $"images/replace/{name}.meta.toml";
                found.TryAdd(name, new PortraitContribution(name, png, mod.FileExists(meta) ? meta : null, true));
            }
        }

        GeneratedInformation info;
        try
        {
            info = new TOMLCollector().Collect(mod);
        }
        catch (Exception)
        {
            // A mod whose files can't be listed contributes nothing here; the normal install
            // reports the real problem.
            return Sorted(found);
        }

        foreach (var group in info.AnimationGroups.Values)
        {
            if (!group.HasAnimation || !group.HasPng) continue;
            var name = "spr_" + group.BaseName;
            if (!PortraitSpriteNames.IsPortrait(name)) continue;
            if (IsUnderReplaceFolder(group.PngRelPath!)) continue;
            found.TryAdd(name, new PortraitContribution(name, group.PngRelPath!, group.AnimationMetaRelPath!.FilePath, false));
        }

        return Sorted(found);
    }

    /// <summary>
    /// What else a portrait pack changes - the hint that its portraits are tied to its own scenes,
    /// dialogue or story. Tags match the in-game text keys (<c>also_dialogue</c> ...).
    /// </summary>
    public static List<string> CompanionContent(IMod mod)
    {
        var tags = new List<string>();
        List<string> Relative(string extension)
        {
            try
            {
                var basePath = mod.GetBasePath().Replace('\\', '/').TrimEnd('/') + "/";
                return mod.GetAllFiles(extension)
                    .Select(p => p.Replace('\\', '/'))
                    .Select(p => basePath.Length > 1 && p.StartsWith(basePath, StringComparison.OrdinalIgnoreCase) ? p[basePath.Length..] : p)
                    .ToList();
            }
            catch (Exception)
            {
                return [];
            }
        }

        var toml = Relative(".toml");
        var mist = Relative(".mist");
        var gml = Relative(".gml");
        var png = Relative(".png");

        if (toml.Any(p => p.StartsWith("t2/", StringComparison.OrdinalIgnoreCase))) tags.Add("dialogue");
        if (mist.Count > 0 || toml.Any(p => p.Contains("cutscene", StringComparison.OrdinalIgnoreCase))) tags.Add("cutscenes");
        if (toml.Any(p => p.StartsWith("fiddle/quests", StringComparison.OrdinalIgnoreCase))) tags.Add("quests");
        if (toml.Any(p => p.StartsWith("fiddle/npcs/", StringComparison.OrdinalIgnoreCase)
                          || p.StartsWith("fiddle/cameos/", StringComparison.OrdinalIgnoreCase))) tags.Add("character_data");
        if (png.Any(p => Path.GetFileName(p).StartsWith("spr_npc_", StringComparison.OrdinalIgnoreCase))) tags.Add("overworld_sprites");
        if (gml.Any(p => p.StartsWith("gml/", StringComparison.OrdinalIgnoreCase))) tags.Add("scripts");
        return tags;
    }

    private static List<PortraitContribution> Sorted(Dictionary<string, PortraitContribution> found) =>
        found.Values.OrderBy(c => c.SpriteName, StringComparer.OrdinalIgnoreCase).ToList();

    private static bool IsUnderReplaceFolder(string relPath)
    {
        var p = relPath.Replace('\\', '/');
        return p.Contains("/replace/", StringComparison.OrdinalIgnoreCase)
               || p.StartsWith("replace/", StringComparison.OrdinalIgnoreCase);
    }
}
