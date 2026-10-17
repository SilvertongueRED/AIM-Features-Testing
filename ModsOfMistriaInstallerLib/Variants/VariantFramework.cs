using System.Text;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;

namespace Garethp.ModsOfMistriaInstallerLib.Variants;

/// <summary>
/// Shared constants and naming rules for the Variants Framework.
///
/// The framework turns mods that replace the same game sprites into separate, switchable
/// "looks" instead of letting the last mod in load order silently win. A look id, a look
/// sprite name and a variant item id are written into save files (on placed furniture, in the
/// framework's per-save sidecar, and as item/object ids), so every rule here is part of a
/// persistent contract: change one only together with a migration.
/// </summary>
public static class VariantFramework
{
    /// <summary>File at the framework mod's root that identifies it, independent of its
    /// manifest name, author or folder name.</summary>
    public const string MarkerFile = "variants_framework.toml";

    /// <summary>The MMAPI mod-data name the in-game runtime declares
    /// (<c>%LOCALAPPDATA%/FieldsOfMistria/mod_data/&lt;name&gt;/</c>).</summary>
    public const string RuntimeName = "mistria_variants";

    /// <summary>The generated runtime registry that replaces the framework's stub.</summary>
    public const string RegistryGmlPath = "gml/data/mistria_variants_registry.gml";

    /// <summary>The generated function the runtime calls; prefixed with the framework's folder
    /// name so AIM's namespace lint accepts it.</summary>
    public const string RegistryFunction = "mistria_variants_registry_data";

    /// <summary>The installer's record of every variant id it has ever generated, kept in the
    /// mods folder beside AIM's other state files so it survives AIM updates.</summary>
    public const string HistoryFileName = "aim_variants.json";

    /// <summary>The look id of the game's own art.</summary>
    public const string VanillaLook = "vanilla";

    public static bool IsFrameworkMod(IMod mod)
    {
        try
        {
            return mod.FileExists(MarkerFile);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The look id for a mod: <c>lk</c> + up to 16 letters/digits of the mod's name part +
    /// a 4 character hash of the full id. It is a valid GML identifier and a valid
    /// snake_case segment (it starts with a letter and has no underscores), so it can be
    /// appended to item ids, object ids, animal keys and sprite names without breaking the
    /// engine's PascalCase/snake_case enum round trip.
    /// </summary>
    public static string LookIdForMod(string modId)
    {
        var id = (modId ?? "").Trim().ToLowerInvariant();
        var namePart = id.Contains('.') ? id[(id.IndexOf('.') + 1)..] : id;
        var letters = new StringBuilder();
        foreach (var c in namePart)
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') letters.Append(c);
            if (letters.Length >= 16) break;
        }

        return "lk" + letters + Hash4(id);
    }

    /// <summary>Four lowercase base-36 characters of FNV-1a over the UTF-8 text.</summary>
    public static string Hash4(string text)
    {
        var hash = Fnv1A(text);
        const string alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
        var builder = new StringBuilder(4);
        for (var i = 0; i < 4; i++)
        {
            builder.Append(alphabet[(int)(hash % 36)]);
            hash /= 36;
        }

        return builder.ToString();
    }

    public static uint Fnv1A(string text)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        foreach (var b in Encoding.UTF8.GetBytes(text ?? ""))
        {
            hash ^= b;
            hash *= prime;
        }

        return hash;
    }

    /// <summary>
    /// True when <paramref name="id"/> is canonical snake_case in the shape the engine's
    /// magic enums round-trip: lowercase segments joined by single underscores, each segment
    /// starting with a letter.
    /// </summary>
    public static bool IsCanonicalId(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        foreach (var segment in id.Split('_'))
        {
            if (segment.Length == 0) return false;
            if (segment[0] is < 'a' or > 'z') return false;
            foreach (var c in segment)
                if (c is not (>= 'a' and <= 'z' or >= '0' and <= '9')) return false;
        }

        return true;
    }

    /// <summary>
    /// A villager portrait (Portrait Wardrobe's domain when that framework is installed). The
    /// shared effect overlays (<c>spr_portrait_effects_*</c>: sweat drops, blush...) are not
    /// portraits anyone picks in Portrait Wardrobe, so they stay this framework's to keep.
    /// </summary>
    public static bool IsPortraitSprite(string sprite) =>
        PortraitWardrobe.PortraitSpriteNames.IsPortrait(sprite);

    /// <summary>The look sprite name for a sprite outside an animal family.</summary>
    public static string LookSpriteName(string vanillaSprite, string lookId) => $"{vanillaSprite}_{lookId}";

    /// <summary>
    /// Where the in-game runtime keeps its settings. AIM reads the player's look choices and
    /// install options from here so a choice made in the game's menu also decides what the next
    /// install bakes in. <c>AIM_FOM_DATA_DIR</c> overrides the FieldsOfMistria data folder (tests).
    /// </summary>
    public static string RuntimeConfigPath()
    {
        var root = Environment.GetEnvironmentVariable("AIM_FOM_DATA_DIR");
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FieldsOfMistria");
        }

        return Path.Combine(root, "mod_data", RuntimeName, RuntimeName + ".json");
    }

    /// <summary>Human readable "Name (Variant)" text for generated items and animal variants.</summary>
    public static string VariantDisplayName(string baseName, string lookName) =>
        $"{baseName} ({lookName})";
}

/// <summary>TomlTable is both a dictionary and a read-only dictionary, which makes the BCL's
/// <c>GetValueOrDefault</c> ambiguous on it.</summary>
public static class TomlTableExtensions
{
    public static object? Get(this Tomlyn.Model.TomlTable table, string key) =>
        table.TryGetValue(key, out var value) ? value : null;
}
