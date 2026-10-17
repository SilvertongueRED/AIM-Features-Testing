namespace Garethp.ModsOfMistriaInstallerLib.PortraitWardrobe;

/// <summary>
/// Naming for the Portrait Wardrobe library.
///
/// The game builds every portrait's sprite name as <c>spr_portrait_{character}_{outfit}_{expression}</c>
/// and reads it once into a per-outfit table. The library keeps each pack's version of a portrait (and
/// the original art) under its own name next to it, so the in-game menu can point the table at
/// whichever one the player picks. The names only need to be unique and stable within one install;
/// the menu looks every one of them up through the registry, never by guessing.
/// </summary>
public static class PortraitSpriteNames
{
    public const string Prefix = "spr_portrait_";

    /// <summary>The key the original art is stored under.</summary>
    public const string VanillaKey = "v";

    /// <summary>
    /// Character portraits only. Effect overlays (<c>spr_portrait_effects_*</c>) are shared by every
    /// character and are not portraits anyone picks between.
    /// </summary>
    public static bool IsPortrait(string spriteName) =>
        spriteName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
        && !spriteName.StartsWith("spr_portrait_effects_", StringComparison.OrdinalIgnoreCase)
        && spriteName.Length > Prefix.Length;

    /// <summary><c>spr_portrait_march_beach_neutral</c> + <c>p3</c> → <c>spr_pw_p3_portrait_march_beach_neutral</c>.</summary>
    public static string VariantName(string key, string spriteName) =>
        $"spr_pw_{key}_{spriteName[4..]}";

    public static string PackKey(int index) => $"p{index}";

    /// <summary>The character a portrait belongs to is the first word after the prefix - enough for folder names.</summary>
    public static string FolderFor(string spriteName)
    {
        var rest = spriteName.Length > Prefix.Length ? spriteName[Prefix.Length..] : spriteName;
        var cut = rest.IndexOf('_');
        var word = cut > 0 ? rest[..cut] : rest;
        return string.IsNullOrWhiteSpace(word) ? "misc" : word.ToLowerInvariant();
    }
}
