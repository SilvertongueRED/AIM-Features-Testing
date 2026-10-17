using System.Text.RegularExpressions;
using Tomlyn.Model;

namespace Garethp.ModsOfMistriaInstallerLib.Variants;

/// <summary>A furniture item generated for one look ("Cottage Garden Arch (Witchy Black)").</summary>
public sealed record ItemVariant(
    string FamilyId,
    string LookId,
    string BaseItem,
    string BaseObject,
    string ItemId,
    string ObjectId,
    bool Retired = false);

/// <summary>An adoption variant generated for one look of an animal variant.</summary>
public sealed record AnimalVariant(
    string FamilyId,
    string LookId,
    string Kind,
    string BaseVariant,
    string VariantId,
    bool Retired = false);

/// <summary>
/// Decides which separate items and adoption variants (the Content-Patcher-style half of the
/// framework) a plan produces. Only objects and items the engine treats generically are cloned:
/// anything the game's code names directly (mailbox, bell, kitchens, ...), fences (which only
/// join identical objects), house stairs and anything with a non-canonical id is left alone.
/// </summary>
public static class VariantContentPlanner
{
    public static (List<ItemVariant> Items, List<AnimalVariant> Animals) Plan(
        VariantPlan plan, PristineAssets pristine)
    {
        var items = new List<ItemVariant>();
        var animals = new List<AnimalVariant>();
        var (deniedObjects, deniedItems) = Denylist(pristine);

        if (plan.Preferences.VariantItems)
        {
            var furnitureToml = FamilyIndex.ParseQuiet(pristine.ReadText("assets/fiddle/object_prototypes/furniture.toml") ?? "");
            foreach (var family in plan.RuntimeFamilies.Where(f => f.Family.Kind == FamilyKind.Furniture))
            {
                var objectId = family.Family.Key;
                if (deniedObjects.Contains(objectId) || !VariantFramework.IsCanonicalId(objectId)) continue;
                if (furnitureToml.TryGetValue(objectId, out var o) && o is TomlTable proto && !IsCloneable(proto)) continue;
                var placing = family.Family.ItemIds.Where(i => !deniedItems.Contains(i) && VariantFramework.IsCanonicalId(i)).ToList();
                if (placing.Count == 0) continue;

                foreach (var look in family.Looks)
                {
                    if (look.Look.Id == family.InstallDefault) continue;
                    if (look.Look.IsVanilla && !plan.Preferences.VariantItemsOriginal) continue;
                    if (!look.Look.IsVanilla && look.Changed.Count == 0) continue;
                    var objectVariant = $"{objectId}_{look.Look.Id}";
                    foreach (var item in placing)
                    {
                        items.Add(new ItemVariant(family.Family.Id, look.Look.Id, item, objectId,
                            $"{item}_{look.Look.Id}", objectVariant));
                        break; // one item per object: pickup returns the first item that places it
                    }
                }
            }
        }

        if (plan.Preferences.VariantAnimals)
        {
            foreach (var family in plan.RuntimeFamilies.Where(f => f.Family.Kind == FamilyKind.Animal))
            {
                var kind = family.Family.Key;
                var toml = FamilyIndex.ParseQuiet(pristine.ReadText($"assets/fiddle/ranching/animals/{kind}.toml") ?? "");
                var variants = AnimalVariants.Read(toml);
                foreach (var look in family.Looks)
                {
                    if (look.Look.IsVanilla || look.Look.Id == family.InstallDefault || look.Changed.Count == 0) continue;
                    foreach (var variant in variants.Where(v => v.Acquirable))
                    {
                        if (!VariantFramework.IsCanonicalId(variant.Key)) continue;
                        if (AnimalVariants.ForLook(variant, look, family.Family, plan) is null) continue;
                        animals.Add(new AnimalVariant(family.Family.Id, look.Look.Id, kind, variant.Key,
                            $"{variant.Key}_{look.Look.Id}"));
                    }
                }
            }
        }

        return (items, animals);
    }

    // Fences auto-tile only with identical objects and stairs must pair; a clone would break both.
    private static bool IsCloneable(TomlTable proto)
    {
        if (proto.TryGetValue("fence", out var f) && f is true) return false;
        if (proto.ContainsKey("house_stairs")) return false;
        if (proto.ContainsKey("interact_menu")) return false;
        return true;
    }

    private static readonly Regex EnumReference = new(@"\b(ObjectId|ItemId)\.([A-Z][A-Za-z0-9]*)\b", RegexOptions.Compiled);

    /// <summary>Objects and items the engine's own code refers to by name: their behaviour is
    /// tied to the id, so a clone would look right but act wrong.</summary>
    public static (HashSet<string> Objects, HashSet<string> Items) Denylist(PristineAssets pristine)
    {
        var objects = new HashSet<string>(StringComparer.Ordinal);
        var items = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in pristine.PathsUnder("assets/gml/"))
        {
            if (!path.EndsWith(".gml", StringComparison.Ordinal)) continue;
            var text = pristine.ReadText(path);
            if (text is null) continue;
            foreach (Match match in EnumReference.Matches(text))
            {
                var snake = PascalToSnake(match.Groups[2].Value);
                (match.Groups[1].Value == "ObjectId" ? objects : items).Add(snake);
            }
        }

        return (objects, items);
    }

    public static string PascalToSnake(string pascal)
    {
        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < pascal.Length; i++)
        {
            var c = pascal[i];
            if (char.IsUpper(c))
            {
                if (i > 0) builder.Append('_');
                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}

/// <summary>Reads animal variants the way the engine resolves them (template + overrides).</summary>
public static class AnimalVariants
{
    public sealed record TypeEntry(string Key, string? TopKey, string? UiKey, object? Lut, object? LutIndex);

    public sealed record Variant(string Key, TomlTable Table, bool Acquirable, Dictionary<string, TypeEntry> Types);

    private static readonly string[] TypeKeys = ["male", "female", "baby"];

    public static List<Variant> Read(TomlTable animal)
    {
        var output = new List<Variant>();
        if (!animal.TryGetValue("variants", out var v) || v is not TomlTable variants) return output;
        var template = variants.TryGetValue("default", out var d) && d is TomlTable def ? def : new TomlTable();

        foreach (var (key, value) in variants)
        {
            if (key == "default" || value is not TomlTable table) continue;
            var acquirable = !(table.TryGetValue("acquirable", out var a) && a is false) &&
                             !(template.TryGetValue("acquirable", out var ta) && ta is false && !table.ContainsKey("acquirable"));
            var types = new Dictionary<string, TypeEntry>(StringComparer.Ordinal);
            var variantLut = table.Get("lut") ?? template.Get("lut");
            var variantLutIndex = table.Get("lut_index") ?? template.Get("lut_index");
            foreach (var type in TypeKeys)
            {
                var entry = table.TryGetValue(type, out var e) && e is TomlTable et ? et : new TomlTable();
                var fallback = template.TryGetValue(type, out var f) && f is TomlTable ft ? ft : new TomlTable();
                var typeKey = (entry.Get("key") ?? fallback.Get("key")) as string;
                if (typeKey is null) continue;
                var topKey = (entry.Get("top_key") ?? fallback.Get("top_key")) as string;
                var uiKey = (entry.Get("ui_key") ?? fallback.Get("ui_key")) as string;
                var lut = entry.Get("lut") ?? variantLut;
                var lutIndex = entry.Get("lut_index") ?? variantLutIndex;
                types[type] = new TypeEntry(typeKey, topKey == "<n/a>" ? null : topKey, uiKey, lut, lutIndex);
            }

            if (types.Count > 0) output.Add(new Variant(key, table, acquirable, types));
        }

        return output;
    }

    /// <summary>
    /// The per-type keys a look variant would use, or null when the look cannot make a complete
    /// variant. A type keeps its vanilla family when the look leaves it untouched; it switches to
    /// the look's family only when the look replaced every sprite of it (the engine refuses an
    /// animal whose animation set is incomplete).
    /// </summary>
    public static Dictionary<string, TypeEntry>? ForLook(Variant variant, FamilyLook look, Family family, VariantPlan plan)
    {
        var kind = family.Key;
        var result = new Dictionary<string, TypeEntry>(StringComparer.Ordinal);
        var anyChanged = false;
        foreach (var (type, entry) in variant.Types)
        {
            var body = FamilySprites(family, kind, entry.Key, plan);
            var top = entry.TopKey is null ? [] : FamilySprites(family, kind, entry.TopKey, plan);
            var bodyState = Coverage(body, look, family, plan);
            var topState = Coverage(top, look, family, plan);
            if (bodyState == Cover.Partial || topState == Cover.Partial) return null;

            var newKey = bodyState == Cover.Full ? entry.Key + "_" + look.Look.Id : entry.Key;
            var newTop = topState == Cover.Full ? entry.TopKey + "_" + look.Look.Id : entry.TopKey;
            var uiKey = entry.UiKey ?? entry.Key;
            var icon = $"spr_ui_icon_animal_{kind}_{uiKey}";
            var newUi = look.Changed.Contains(icon) && look.Map.ContainsKey(icon) ? uiKey + "_" + look.Look.Id : uiKey;
            if (bodyState == Cover.Full || topState == Cover.Full) anyChanged = true;
            result[type] = new TypeEntry(newKey, newTop, newUi, entry.Lut, entry.LutIndex);
        }

        return anyChanged ? result : null;
    }

    private enum Cover { None, Partial, Full }

    private static List<string> FamilySprites(Family family, string kind, string key, VariantPlan plan)
    {
        var animations = plan.AnimalAnimations.GetValueOrDefault(kind) ?? [];
        return family.Sprites
            .Where(s => AnimalSpriteName.TryParse(s, kind, animations, out var parts) && parts.Key == key)
            .ToList();
    }

    private static Cover Coverage(List<string> sprites, FamilyLook look, Family family, VariantPlan plan)
    {
        if (sprites.Count == 0) return Cover.None;
        var covered = 0;
        foreach (var sprite in sprites)
        {
            var target = look.Map.GetValueOrDefault(sprite);
            var expected = VariantPlanner.LookSpriteName(family, sprite, look.Look.Id, plan);
            if (target == expected) covered++;
        }

        if (covered == 0) return Cover.None;
        return covered == sprites.Count ? Cover.Full : Cover.Partial;
    }
}
