using System.Text.Json;

namespace Garethp.ModsOfMistriaInstallerLib.Variants;

/// <summary>
/// The player's choices, read from the in-game runtime's own config file
/// (<see cref="VariantFramework.RuntimeConfigPath"/>). The game writes it; AIM only reads it,
/// so a missing or damaged file simply means "defaults" and never blocks an install.
/// </summary>
public sealed class VariantPreferences
{
    /// <summary>Family id to the look the player chose as the default for everything of that
    /// kind. Decides which look the base sprite holds after install.</summary>
    public Dictionary<string, string> GlobalLooks { get; } = new(StringComparer.Ordinal);

    /// <summary>Keep the game's original art as a selectable look even when only one mod
    /// changes an object.</summary>
    public bool KeepOriginalLooks { get; set; } = true;

    /// <summary>Add each mod's version of a furniture item to shops and crafting as its own
    /// item ("Cottage Garden Arch (Witchy Black)").</summary>
    public bool VariantItems { get; set; } = true;

    /// <summary>Also add an "(Original)" item when a mod's look is the default.</summary>
    public bool VariantItemsOriginal { get; set; }

    /// <summary>Add each mod's version of an animal as its own adoption variant.</summary>
    public bool VariantAnimals { get; set; } = true;

    public static VariantPreferences Load(string? path = null)
    {
        var preferences = new VariantPreferences();
        path ??= VariantFramework.RuntimeConfigPath();
        try
        {
            if (!File.Exists(path)) return preferences;
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
            return FromJson(doc.RootElement);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Logger.Log($"[variants] Ignoring unreadable look preferences at '{path}': {exception.Message}");
            return preferences;
        }
    }

    public static VariantPreferences FromJson(JsonElement root)
    {
        var preferences = new VariantPreferences();
        if (root.ValueKind != JsonValueKind.Object) return preferences;

        if (root.TryGetProperty("global_looks", out var looks) && looks.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in looks.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(property.Value.GetString()))
                    preferences.GlobalLooks[property.Name] = property.Value.GetString()!;
            }
        }

        if (root.TryGetProperty("install", out var install) && install.ValueKind == JsonValueKind.Object)
        {
            preferences.KeepOriginalLooks = ReadBool(install, "keep_original_looks", preferences.KeepOriginalLooks);
            preferences.VariantItems = ReadBool(install, "variant_items", preferences.VariantItems);
            preferences.VariantItemsOriginal = ReadBool(install, "variant_items_original", preferences.VariantItemsOriginal);
            preferences.VariantAnimals = ReadBool(install, "variant_animals", preferences.VariantAnimals);
        }

        return preferences;
    }

    private static bool ReadBool(JsonElement parent, string name, bool fallback) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;
}
