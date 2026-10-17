using System.Text.Json;
using System.Text.Json.Nodes;

namespace Garethp.ModsOfMistriaInstallerLib.Variants;

/// <summary>
/// Every variant item and adoption variant AIM has ever generated, kept in
/// <c>mods/aim_variants.json</c>. A save stores those ids, and the game drops placed objects it
/// no longer knows (their chest contents and table tops with them) and refuses a save whose
/// "used today" list names an unknown object. So when a look's mod is removed, AIM keeps
/// generating a harmless "retired" stand-in under the same id instead of forgetting it.
/// </summary>
public sealed class VariantHistory
{
    public Dictionary<string, ItemVariant> Items { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, AnimalVariant> Animals { get; } = new(StringComparer.Ordinal);

    /// <summary>The file existed but could not be read; <see cref="Save"/> keeps a copy of it
    /// before replacing it, so the ids it lists can still be recovered by hand.</summary>
    public bool Unreadable { get; private set; }

    private static string AnimalKey(AnimalVariant a) => a.Kind + "/" + a.VariantId;

    public static string PathIn(string modsFolder) => Path.Combine(modsFolder, VariantFramework.HistoryFileName);

    public static VariantHistory Load(string? modsFolder)
    {
        var history = new VariantHistory();
        if (string.IsNullOrEmpty(modsFolder)) return history;
        var path = PathIn(modsFolder);
        try
        {
            if (!File.Exists(path)) return history;
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (root is null) return history;

            if (root["items"] is JsonArray items)
            {
                foreach (var node in items.OfType<JsonObject>())
                {
                    var item = Str(node, "item");
                    var obj = Str(node, "object");
                    var baseItem = Str(node, "base_item");
                    var baseObject = Str(node, "base_object");
                    if (item is null || obj is null || baseItem is null || baseObject is null) continue;
                    history.Items[item] = new ItemVariant(Str(node, "family") ?? "", Str(node, "look") ?? "",
                        baseItem, baseObject, item, obj);
                }
            }

            if (root["animals"] is JsonArray animals)
            {
                foreach (var node in animals.OfType<JsonObject>())
                {
                    var kind = Str(node, "kind");
                    var variant = Str(node, "variant");
                    var baseVariant = Str(node, "base");
                    if (kind is null || variant is null || baseVariant is null) continue;
                    var animal = new AnimalVariant(Str(node, "family") ?? "", Str(node, "look") ?? "", kind,
                        baseVariant, variant);
                    history.Animals[AnimalKey(animal)] = animal;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException
                                              or InvalidOperationException or ArgumentException)
        {
            history.Unreadable = true;
            Logger.Log($"[variants] Could not read {path}: {exception.Message}. Removed looks may not be kept loadable this time.");
        }

        return history;
    }

    /// <summary>True when an earlier install generated this adoption variant (saves may hold it).</summary>
    public bool Knows(AnimalVariant animal) => Animals.ContainsKey(AnimalKey(animal));

    /// <summary>Variants generated before but not this time: they become retired stand-ins.</summary>
    public (List<ItemVariant> Items, List<AnimalVariant> Animals) Retired(
        IEnumerable<ItemVariant> currentItems, IEnumerable<AnimalVariant> currentAnimals)
    {
        var itemIds = currentItems.Select(i => i.ItemId).ToHashSet(StringComparer.Ordinal);
        var animalKeys = currentAnimals.Select(AnimalKey).ToHashSet(StringComparer.Ordinal);
        return (
            Items.Values.Where(i => !itemIds.Contains(i.ItemId)).Select(i => i with { Retired = true }).ToList(),
            Animals.Values.Where(a => !animalKeys.Contains(AnimalKey(a))).Select(a => a with { Retired = true }).ToList());
    }

    public void Remember(IEnumerable<ItemVariant> items, IEnumerable<AnimalVariant> animals)
    {
        foreach (var item in items) Items[item.ItemId] = item with { Retired = false };
        foreach (var animal in animals) Animals[AnimalKey(animal)] = animal with { Retired = false };
    }

    public void Save(string? modsFolder)
    {
        if (string.IsNullOrEmpty(modsFolder) || !Directory.Exists(modsFolder)) return;
        var root = new JsonObject
        {
            ["schema"] = 1,
            ["note"] = "Written by AIM's Variants Framework support. Keeps saves loadable when a look's mod is removed. Safe to keep; do not edit.",
            ["items"] = new JsonArray(Items.Values.OrderBy(i => i.ItemId, StringComparer.Ordinal).Select(i => (JsonNode)new JsonObject
            {
                ["item"] = i.ItemId, ["object"] = i.ObjectId, ["base_item"] = i.BaseItem,
                ["base_object"] = i.BaseObject, ["look"] = i.LookId, ["family"] = i.FamilyId,
            }).ToArray()),
            ["animals"] = new JsonArray(Animals.Values.OrderBy(AnimalKey, StringComparer.Ordinal).Select(a => (JsonNode)new JsonObject
            {
                ["kind"] = a.Kind, ["variant"] = a.VariantId, ["base"] = a.BaseVariant,
                ["look"] = a.LookId, ["family"] = a.FamilyId,
            }).ToArray()),
        };

        var path = PathIn(modsFolder);
        var temp = path + ".tmp";
        try
        {
            if (Unreadable && File.Exists(path)) File.Copy(path, path + ".unreadable", overwrite: true);
            File.WriteAllText(temp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Logger.Log($"[variants] Could not save {path}: {exception.Message}");
        }
    }

    private static string? Str(JsonObject node, string key) =>
        node[key] is JsonValue value && value.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;
}
