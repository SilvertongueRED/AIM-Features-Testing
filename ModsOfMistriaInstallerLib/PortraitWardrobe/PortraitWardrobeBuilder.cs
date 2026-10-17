using Garethp.ModsOfMistriaInstallerLib.Models.SDK;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;
using Garethp.ModsOfMistriaInstallerLib.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using Tomlyn;
using Tomlyn.Model;

namespace Garethp.ModsOfMistriaInstallerLib.PortraitWardrobe;

/// <summary>Where a pack came from on Nexus, when AIM knows.</summary>
public sealed record PortraitPackSource(int ModId, string PageUrl);

/// <summary>What one install of the portrait library produced.</summary>
public sealed class PortraitLibraryResult
{
    /// <summary>The kept portraits, to run through the normal installers. Empty when nothing needed keeping.</summary>
    public required PortraitVariantMod VariantMod { get; init; }

    /// <summary>The registry the in-game menu reads (fiddle/mods/portrait_wardrobe/registry.toml).</summary>
    public required string RegistryToml { get; init; }

    public required string RegistryJson { get; init; }

    public int PackCount { get; init; }

    public int SpriteCount { get; init; }

    /// <summary>Problems worth telling the user about (a broken PNG, an unreadable meta...).</summary>
    public List<string> Notes { get; } = [];
}

/// <summary>
/// Builds the Portrait Wardrobe library for one install.
///
/// Without it, every portrait pack writes into the same sprite names and the last mod in load order
/// wins; the others are gone from the game. The library keeps them all: for every portrait a pack
/// touches, each pack's version (and the original art) is installed as its own sprite next to the
/// game's, in the same atlas the game loads that portrait from, and a registry records which sprite
/// is which. The in-game menu then points the game's portrait tables at whichever the player picks.
///
/// The normal install is not changed at all: the game's own sprite still ends up with exactly what
/// it would have without the library, so an install without the in-game mod behaves as before.
///
/// Copies are only made where they are needed:
///   - the original art, for every portrait any pack replaces (so "original" can be picked back);
///   - each pack's version, when two or more packs touch the same portrait, or when one pack
///     overrides a game portrait by shipping a same-named sprite (which version the game shows then
///     is not certain, so the pick must not depend on it).
/// A single pack replacing a portrait through images/replace, or adding a brand-new one, is already
/// unambiguously that pack's art under the game's own name, and is used as is.
/// </summary>
public sealed class PortraitWardrobeBuilder(
    IPortraitVanillaSource vanilla,
    Func<IMod, PortraitPackSource?>? nexusSourceOf = null,
    PortraitRequirementsCache? requirementsCache = null,
    INexusRequirementsFetcher? requirementsFetcher = null)
{
    public const int RegistryFormat = 1;

    /// <summary>The registry file, as an asset path (relative to the game folder).</summary>
    public static readonly string RegistryRelativePath = "fiddle/mods/portrait_wardrobe/registry.toml";

    /// <summary>
    /// The library is only built when Portrait Wardrobe (the in-game half) is being installed. It is
    /// recognised by its placeholder registry rather than its name, so a renamed or forked copy works.
    /// </summary>
    public static bool IsFramework(IMod mod)
    {
        try
        {
            return mod.FileExists(RegistryRelativePath);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool IsFrameworkInstalled(IEnumerable<IMod> mods) => mods.Any(IsFramework);

    private sealed class Pack
    {
        public required string Key { get; init; }
        public required IMod Mod { get; init; }
        public required int Order { get; init; }
        public required List<PortraitContribution> Contributions { get; init; }
        public PortraitPackSource? Source { get; init; }
    }

    public PortraitLibraryResult Build(IReadOnlyList<IMod> installMods, string builderName, DateTimeOffset now)
    {
        var notes = new List<string>();

        // ── packs, in install (load) order ──
        var packs = new List<Pack>();
        for (var i = 0; i < installMods.Count; i++)
        {
            var mod = installMods[i];
            if (mod is PortraitVariantMod || IsFramework(mod)) continue;
            List<PortraitContribution> contributions;
            try
            {
                contributions = PortraitScanner.Scan(mod);
            }
            catch (Exception e)
            {
                notes.Add($"Couldn't read portraits from {mod.GetName()}: {e.Message}");
                continue;
            }
            if (contributions.Count == 0) continue;

            PortraitPackSource? source = null;
            try { source = nexusSourceOf?.Invoke(mod); } catch (Exception) { source = null; }

            packs.Add(new Pack
            {
                Key = PortraitSpriteNames.PackKey(packs.Count),
                Mod = mod,
                Order = i,
                Contributions = contributions,
                Source = source,
            });
        }

        // ── requirements from Nexus (one request, cached) ──
        var nexusIds = packs.Where(p => p.Source is not null).Select(p => p.Source!.ModId).ToList();
        try
        {
            requirementsCache?.Refresh(nexusIds, requirementsFetcher, now);
        }
        catch (Exception)
        {
            // requirements are a hint; never fail the library over them
        }

        // ── which packs touch which portrait ──
        var bySprite = new SortedDictionary<string, List<(Pack Pack, PortraitContribution Contribution)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in packs)
        {
            foreach (var c in pack.Contributions)
            {
                if (!bySprite.TryGetValue(c.SpriteName, out var list))
                    bySprite[c.SpriteName] = list = [];
                list.Add((pack, c));
            }
        }

        // ── keep what needs keeping ──
        var variants = new PortraitVariantMod();
        var sprites = new JObject();
        foreach (var (packSpelling, sources) in bySprite)
        {
            // The game names portraits in lower case and looks them up by exact name, so the registry
            // does too, whatever case a pack spelled its file in.
            var spriteName = packSpelling.ToLowerInvariant();
            var vanillaMetaText = SafeVanillaMeta(spriteName);
            var vanillaMeta = ParseMeta(vanillaMetaText);
            var hasVanilla = vanillaMeta?.Asset is not null;
            var folder = PortraitSpriteNames.FolderFor(spriteName);

            // A pack spelling the name differently may not land in the game's sprite at all, so keep
            // its own copy rather than trust the game's sprite to be its art.
            var oddCase = sources.Any(s => !string.Equals(s.Contribution.SpriteName, spriteName, StringComparison.Ordinal));
            var copyEach = sources.Count >= 2 || oddCase || (hasVanilla && !sources[0].Contribution.IsReplacement);
            var src = new JObject();

            // Frame layout to go by: the game's own, else the first pack that ships a meta for it
            // (a pack can replace a portrait another pack adds, without a meta of its own).
            var reference = vanillaMeta;
            if (reference?.Asset is null)
            {
                foreach (var (refPack, refContribution) in sources)
                {
                    if (refContribution.MetaPath is null) continue;
                    try
                    {
                        reference = TomlSerializer.Deserialize<SpriteMetaFile>(refPack.Mod.ReadFile(refContribution.MetaPath));
                    }
                    catch (Exception)
                    {
                        reference = null;
                    }
                    if (reference?.Asset is not null) break;
                }
            }

            foreach (var (pack, contribution) in sources)
            {
                if (!copyEach)
                {
                    // Used under the game's own name - but only if the installer will actually put it
                    // there: it skips a strip that doesn't divide into the portrait's frames, and the
                    // menu must not offer art that never made it in.
                    if (!MakeVariant(pack.Mod, contribution, reference, hasVanilla, out _, out _, out var skipWhy))
                    {
                        notes.Add($"{pack.Mod.GetName()}: {spriteName} isn't offered in the menu ({skipWhy}).");
                        continue;
                    }
                    src[pack.Key] = spriteName;
                    continue;
                }

                var variantName = PortraitSpriteNames.VariantName(pack.Key, spriteName);
                var made = MakeVariant(pack.Mod, contribution, reference, hasVanilla, out var meta, out var png, out var why);
                if (!made)
                {
                    notes.Add($"{pack.Mod.GetName()}: {spriteName} left out of the portrait library ({why}).");
                    continue;
                }
                variants.AddSprite(folder, variantName, meta!, () => png!);
                src[pack.Key] = variantName;
            }

            if (src.Count == 0 && !hasVanilla) continue;

            var entry = new JObject { ["src"] = src };
            entry["winner"] = sources[^1].Pack.Key;

            if (hasVanilla)
            {
                var vanillaName = PortraitSpriteNames.VariantName(PortraitSpriteNames.VanillaKey, spriteName);
                var vanillaVariantMeta = VariantMeta(vanillaMeta!.Asset!, vanillaMeta.Asset!.FrameWidth, vanillaMeta.Asset.FrameHeight,
                    Math.Max(1, vanillaMeta.Asset.FrameCount ?? 1), Atlas.CanonicalType(vanillaMeta.Asset.Atlas) ?? "Npc");
                var name = spriteName;
                variants.AddSprite(folder, vanillaName, vanillaVariantMeta, () =>
                    vanilla.Png(name) ?? throw new InvalidDataException($"original art for {name} is missing from the backup"));
                entry["vanilla"] = vanillaName;
            }

            sprites[spriteName] = entry;
        }

        // ── registry ──
        var packArray = new JArray();
        // What counts as installed: by id, by Nexus page (from AIM's download records or the mod's own
        // manifest links), or by name - a requirement unzipped by hand has no download record.
        var installedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var installedNames = new HashSet<string>(StringComparer.Ordinal);
        var installedNexus = new HashSet<int>();
        foreach (var mod in installMods)
        {
            try { installedIds.Add(mod.GetId()); } catch (Exception) { }
            try { installedNames.Add(NormalizeName(mod.GetName())); } catch (Exception) { }
            try
            {
                var s = nexusSourceOf?.Invoke(mod);
                if (s is not null) installedNexus.Add(s.ModId);
            }
            catch (Exception) { }
            try
            {
                if (NexusIdFromUrl(mod.GetDownloadUrl()) is { } fromDownload) installedNexus.Add(fromDownload);
                if (NexusIdFromUrl(mod.GetUpdateUrl()) is { } fromUpdate) installedNexus.Add(fromUpdate);
            }
            catch (Exception) { }
        }
        installedNames.Remove("");

        foreach (var pack in packs)
        {
            var requirements = new JArray();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var req in pack.Mod.GetRequirements())
            {
                if (!seen.Add(req.Name)) continue;
                requirements.Add(new JObject
                {
                    ["name"] = req.Name,
                    ["url"] = req.DownloadUrl ?? "",
                    ["installed"] = installedIds.Contains(req.GetId()) || installedNames.Contains(NormalizeName(req.Name)),
                    ["source"] = "manifest",
                    ["notes"] = "",
                });
            }
            if (pack.Source is not null && requirementsCache?.Get(pack.Source.ModId) is { } nexusReqs)
            {
                foreach (var req in nexusReqs)
                {
                    if (req is null) continue;
                    if (req.ModId == NexusRequirementsApi.InstallerModId) continue; // AIM is the installer
                    if (!seen.Add(req.ModName)) continue;
                    requirements.Add(new JObject
                    {
                        ["name"] = req.ModName,
                        ["url"] = NexusRequirementsApi.LinkFor(req),
                        ["installed"] = !req.External
                                        && (installedNexus.Contains(req.ModId) || installedNames.Contains(NormalizeName(req.ModName))),
                        ["source"] = "nexus",
                        ["notes"] = req.Notes,
                    });
                }
            }

            packArray.Add(new JObject
            {
                ["key"] = pack.Key,
                ["id"] = pack.Mod.GetId(),
                ["name"] = pack.Mod.GetName(),
                ["author"] = pack.Mod.GetAuthor(),
                ["version"] = pack.Mod.GetVersion(),
                ["order"] = pack.Order,
                ["nexus_id"] = pack.Source?.ModId ?? 0,
                ["url"] = pack.Source?.PageUrl ?? SafeUrl(pack.Mod.GetDownloadUrl()),
                ["also"] = new JArray(SafeCompanion(pack.Mod)),
                ["requirements"] = requirements,
            });
        }

        var registry = new JObject
        {
            ["format"] = RegistryFormat,
            ["built"] = true,
            ["builder"] = builderName,
            ["packs"] = packArray,
            ["sprites"] = sprites,
            ["notes"] = new JArray(notes.Take(50)),
        };

        var json = registry.ToString(Formatting.None);
        var result = new PortraitLibraryResult
        {
            VariantMod = variants,
            RegistryJson = json,
            RegistryToml = RegistryToml(json),
            PackCount = packs.Count,
            SpriteCount = sprites.Count,
        };
        result.Notes.AddRange(notes);
        return result;
    }

    /// <summary>The fiddle file: the JSON as one TOML string, read in-game with fiddle_get + json_parse.</summary>
    public static string RegistryToml(string json)
    {
        var table = new TomlTable
        {
            ["format"] = (long)RegistryFormat,
            ["data"] = json,
        };
        return "# Built by AIM for Portrait Wardrobe. Rebuilt on every install - don't edit.\n"
               + TomlSerializer.Serialize(table);
    }

    // ── variants ──

    private static bool MakeVariant(IMod mod, PortraitContribution contribution, SpriteMetaFile? reference, bool referenceIsGame,
        out string? metaToml, out byte[]? png, out string why)
    {
        metaToml = null;
        png = null;
        why = "";

        var asset = new SpriteMetaFileAssetProperties();
        if (reference?.Asset is not null) asset.Merge(reference.Asset);
        if (contribution.MetaPath is not null)
        {
            SpriteMetaFile? modMeta;
            try
            {
                modMeta = TomlSerializer.Deserialize<SpriteMetaFile>(mod.ReadFile(contribution.MetaPath));
            }
            catch (Exception e)
            {
                why = $"its .meta.toml can't be read: {e.Message}";
                return false;
            }
            asset.Merge(modMeta?.Asset);
        }
        if (asset.FrameWidth <= 0 || asset.FrameHeight <= 0)
        {
            why = "no frame size to go by";
            return false;
        }

        try
        {
            using var stream = mod.ReadFileAsStream(contribution.PngPath);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            png = buffer.ToArray();
        }
        catch (Exception e)
        {
            why = $"its PNG can't be read: {e.Message}";
            return false;
        }

        ImageInfo info;
        try
        {
            info = Image.Identify(new MemoryStream(png));
        }
        catch (Exception e)
        {
            why = $"its PNG is not a valid image: {e.Message}";
            png = null;
            return false;
        }

        var frames = Math.Max(1, asset.FrameCount ?? 1);
        var frameWidth = asset.FrameWidth;
        var frameHeight = asset.FrameHeight;
        if (info.Width != frameWidth * frames || info.Height != frameHeight)
        {
            // Same rule the installer uses for replacements: resize frames when the strip divides.
            if (info.Width % frames != 0)
            {
                why = $"image width {info.Width} doesn't divide into {frames} frame(s)";
                png = null;
                return false;
            }
            frameWidth = info.Width / frames;
            frameHeight = info.Height;
        }

        // Live with the game's own copy of this portrait, so it loads whenever the game's does.
        var atlas = referenceIsGame && reference?.Asset?.Atlas is { Length: > 0 } gameAtlas
            ? Atlas.CanonicalType(gameAtlas)!
            : Atlas.CanonicalType(asset.Atlas) ?? "Npc";

        metaToml = VariantMeta(asset, frameWidth, frameHeight, frames, atlas);
        return true;
    }

    private static string VariantMeta(SpriteMetaFileAssetProperties asset, int frameWidth, int frameHeight, int frames, string atlas)
    {
        var props = new TomlTable
        {
            ["frame_size"] = new TomlArray { (long)frameWidth, (long)frameHeight },
            ["frame_len"] = (long)frames,
            ["dimensions"] = new TomlArray { (long)(frameWidth * frames), (long)frameHeight },
            ["atlas"] = atlas,
        };
        if (asset.Duration is { Count: 1 }) props["duration"] = asset.Duration[0];
        else if (asset.Duration is { Count: > 1 })
        {
            var durations = new TomlArray();
            foreach (var d in asset.Duration) durations.Add(d);
            props["duration"] = durations;
        }
        if (asset.Offset is not null && (asset.Offset.Horizontal is not null || asset.Offset.Vertical is not null))
        {
            var offset = new TomlTable();
            if (asset.Offset.Horizontal is not null) offset["horizontal"] = asset.Offset.Horizontal;
            if (asset.Offset.Vertical is not null) offset["vertical"] = asset.Offset.Vertical;
            props["offset"] = offset;
        }

        var table = new TomlTable
        {
            ["meta_properties"] = new TomlTable { ["asset_kind"] = "Animation" },
            ["asset_properties"] = props,
        };
        return TomlSerializer.Serialize(table);
    }

    // ── helpers ──

    private string? SafeVanillaMeta(string spriteName)
    {
        try { return vanilla.Meta(spriteName); }
        catch (Exception) { return null; }
    }

    private static SpriteMetaFile? ParseMeta(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return TomlSerializer.Deserialize<SpriteMetaFile>(text); }
        catch (Exception) { return null; }
    }

    private static List<string> SafeCompanion(IMod mod)
    {
        try { return PortraitScanner.CompanionContent(mod); }
        catch (Exception) { return []; }
    }

    /// <summary>Lower case letters and digits only: "March Expanded!" and "march-expanded" match.</summary>
    internal static string NormalizeName(string? name) =>
        new((name ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static readonly System.Text.RegularExpressions.Regex NexusModUrl = new(
        @"nexusmods\.com/" + NexusRequirementsApi.GameDomain + @"/mods/(\d+)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>The Nexus mod id in a fieldsofmistria mod page link, if it is one.</summary>
    internal static int? NexusIdFromUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var match = NexusModUrl.Match(url);
        return match.Success && int.TryParse(match.Groups[1].Value, out var id) ? id : null;
    }

    private static string SafeUrl(string? url) =>
        url is not null && (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                            || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) ? url : "";
}
