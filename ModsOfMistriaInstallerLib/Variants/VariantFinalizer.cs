using Garethp.ModsOfMistriaInstallerLib.Security;
using Garethp.ModsOfMistriaInstallerLib.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tomlyn.Model;
using Toml = Garethp.ModsOfMistriaInstallerLib.Utils.Toml;

namespace Garethp.ModsOfMistriaInstallerLib.Variants;

/// <summary>
/// Runs after every mod's content is merged, so clones copy each object, item and animal as
/// the selected mods left it. Writes: shadow and outline links for look sprites, variant items
/// and their objects, shop entries next to each original, adoption variants, and retired
/// stand-ins for variants whose look's mod is gone.
/// </summary>
public static class VariantFinalizer
{
    public const string VariantItemsFile = "fiddle/items/furniture/mvf_variants.toml";

    public static void Apply(IFileModifier files, VariantSession session, Action<string, string> report)
    {
        var plan = session.Plan;
        // The registry (which lists the plan's warnings) was rendered before anything installed,
        // so what goes wrong from here on is only ever seen in the log.
        var warningsBefore = plan.Warnings.Count;
        try
        {
            ApplyInner(files, session, report);
        }
        finally
        {
            foreach (var warning in plan.Warnings.Skip(warningsBefore).Take(200))
                Logger.Log("[variants] " + warning);
        }
    }

    private static void ApplyInner(IFileModifier files, VariantSession session, Action<string, string> report)
    {
        var plan = session.Plan;
        var staged = StagedSprites(files);
        try
        {
            LinkManifest(files, "data_files/animation/shadow_manifest.json", plan.ShadowLinks, staged);
            LinkManifest(files, "data_files/animation/outlines.json", plan.OutlineLinks, staged);
        }
        catch (Exception exception)
        {
            // Costs the new looks their shadows and outlines, never the install.
            session.Plan.Warnings.Add($"Shadows and outlines of new looks were not linked: {exception.Message}");
            Logger.Log($"[variants] Linking shadows and outlines failed: {exception}");
        }

        try
        {
            WriteItemVariants(files, session, staged, report);
        }
        catch (Exception exception)
        {
            session.Plan.Warnings.Add($"Variant items were not added: {exception.Message}");
            Logger.Log($"[variants] Variant items failed: {exception}");
        }

        try
        {
            WriteAnimalVariants(files, session, staged, report);
        }
        catch (Exception exception)
        {
            session.Plan.Warnings.Add($"Animal variants were not added: {exception.Message}");
            Logger.Log($"[variants] Animal variants failed: {exception}");
        }
    }

    // Every sprite the rebuilt archive defines. The engine resolves fiddle sprite names strictly
    // at boot, so a clone may only name sprites that really landed (a mod skipped for a GML
    // problem takes its look sprites with it).
    private static HashSet<string> StagedSprites(IFileModifier files)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in files.FindFiles("assets/", ".meta.toml"))
        {
            var normalized = path.Replace('\\', '/');
            var file = normalized[(normalized.LastIndexOf('/') + 1)..];
            if (!file.EndsWith(".meta.toml", StringComparison.OrdinalIgnoreCase)) continue;
            var stem = file[..^".meta.toml".Length];
            if (stem.StartsWith("spr_", StringComparison.Ordinal)) names.Add(stem);
        }

        return names;
    }

    private static string? MissingSprite(TomlTable table, HashSet<string> staged) =>
        FamilyIndex.SpriteStrings(table).FirstOrDefault(sprite => !staged.Contains(sprite));

    // ── manifests ────────────────────────────────────────────────────────────

    // Only sprites that landed are linked: the game logs (and a debug build crashes on) every
    // manifest entry naming a sprite it does not have.
    private static void LinkManifest(IFileModifier files, string relPath, IDictionary<string, string> links,
        HashSet<string> staged)
    {
        if (links.Count == 0) return;
        var path = InputSafety.AssetPath(relPath);
        JObject manifest;
        try
        {
            manifest = files.Exists(path) ? JObject.Parse(files.Read(path)) : new JObject();
        }
        catch (JsonException)
        {
            return;
        }

        var changed = false;
        foreach (var (sprite, target) in links)
        {
            if (manifest.ContainsKey(sprite)) continue; // a mod's own mapping wins
            if (!staged.Contains(sprite) || !staged.Contains(target)) continue;
            manifest[sprite] = target;
            changed = true;
        }

        if (changed) files.Write(path, manifest.ToString(Formatting.Indented));
    }

    // ── items ────────────────────────────────────────────────────────────────

    private static void WriteItemVariants(IFileModifier files, VariantSession session, HashSet<string> staged,
        Action<string, string> report)
    {
        var all = session.ItemVariants.Concat(session.RetiredItems).ToList();
        if (all.Count == 0) return;

        var furniturePath = InputSafety.AssetPath("fiddle/object_prototypes/furniture.toml");
        if (!files.Exists(furniturePath)) return;
        var furniture = Toml.ParseToml(files.Read(furniturePath));
        var items = IndexItems(files);

        var output = new TomlTable();
        var written = new List<ItemVariant>();
        foreach (var planned in all)
        {
            var variant = planned;
            if (furniture.ContainsKey(variant.ObjectId) || items.ContainsKey(variant.ItemId))
            {
                session.Plan.Warnings.Add($"'{variant.ItemId}' already exists in the game data; its variant was not generated.");
                continue;
            }

            if (furniture.Get(variant.BaseObject) is not TomlTable baseObject ||
                !items.TryGetValue(variant.BaseItem, out var baseItem))
            {
                if (variant.Retired || session.History.Items.ContainsKey(variant.ItemId))
                    session.Plan.Warnings.Add($"'{variant.ItemId}' could not be kept loadable: its original '{variant.BaseItem}' is gone.");
                continue;
            }

            var objectClone = VariantPlanner.CloneTable(baseObject);
            var itemClone = VariantPlanner.CloneTable(baseItem);
            if (!variant.Retired)
            {
                string? problem;
                if (session.Plan.FindLook(variant.FamilyId, variant.LookId) is not { } look)
                {
                    problem = "its look is not part of this install";
                }
                else
                {
                    RemapSprites(objectClone, look);
                    RemapSprites(itemClone, look);
                    problem = (MissingSprite(objectClone, staged) ?? MissingSprite(itemClone, staged)) is { } image
                        ? $"its image '{image}' is not installed"
                        : null;
                }

                if (problem is not null)
                {
                    session.Plan.Warnings.Add($"'{variant.ItemId}' was not added: {problem}.");

                    // An earlier install generated this id, so saves may hold it: keep it loadable
                    // as a retired stand-in rather than let the game drop the placed piece (and its
                    // chest contents) because one mod's art did not land this time.
                    if (!session.History.Items.ContainsKey(variant.ItemId)) continue;
                    variant = variant with { Retired = true };
                    objectClone = VariantPlanner.CloneTable(baseObject);
                    itemClone = VariantPlanner.CloneTable(baseItem);
                }
            }

            if (variant.Retired && (MissingSprite(objectClone, staged) ?? MissingSprite(itemClone, staged)) is { } gone)
            {
                session.Plan.Warnings.Add($"'{variant.ItemId}' could not be kept loadable: the original's image '{gone}' is not installed.");
                continue;
            }

            furniture[variant.ObjectId] = objectClone;
            itemClone["object"] = variant.ObjectId;
            var baseName = baseItem.Get("name") as string ?? FamilyIndex.Prettify(variant.BaseItem);
            if (variant.Retired)
            {
                itemClone["name"] = VariantFramework.VariantDisplayName(baseName, "retired look");
                itemClone["recipe_key"] = "mvf_retired_" + variant.ItemId;
                if (itemClone.Get("tags") is TomlArray tags)
                {
                    var kept = new TomlArray();
                    foreach (var tag in tags.OfType<string>().Where(t => t == "furniture")) kept.Add(tag);
                    itemClone["tags"] = kept;
                }
            }
            else
            {
                var lookName = session.Plan.Looks.GetValueOrDefault(variant.LookId)?.Name ?? variant.LookId;
                itemClone["name"] = VariantFramework.VariantDisplayName(baseName, lookName);
                if (!itemClone.ContainsKey("recipe_key")) itemClone["recipe_key"] = variant.BaseItem;
            }

            output[variant.ItemId] = itemClone;
            written.Add(variant);
        }

        if (written.Count == 0) return;
        files.Write(furniturePath, Toml.SerializeGameToml(furniture));

        var itemsPath = InputSafety.AssetPath(VariantItemsFile);
        if (files.Exists(itemsPath))
        {
            var existing = Toml.ParseToml(files.Read(itemsPath));
            foreach (var (key, value) in output) existing[key] = value;
            output = existing;
        }

        files.Write(itemsPath, Toml.SerializeGameToml(output));
        session.WrittenItemVariants.AddRange(written);

        var forShops = written.Where(v => !v.Retired)
            .GroupBy(v => v.BaseItem, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(v => v.ItemId).ToList(), StringComparer.Ordinal);
        foreach (var shopFile in new[] { "fiddle/stores.toml", "fiddle/festivals.toml" })
            InsertIntoShops(files, InputSafety.AssetPath(shopFile), forShops);

        report($"Variants Framework: added {written.Count(v => !v.Retired)} variant item(s), kept {written.Count(v => v.Retired)} retired item(s) loadable", "");
    }

    private static Dictionary<string, TomlTable> IndexItems(IFileModifier files)
    {
        var index = new Dictionary<string, TomlTable>(StringComparer.Ordinal);
        foreach (var path in files.FindFiles("assets/fiddle/items/", ".toml")
                     .Where(p => !p.EndsWith(".meta.toml", StringComparison.OrdinalIgnoreCase))
                     .Order(StringComparer.Ordinal))
        {
            TomlTable table;
            try
            {
                table = Toml.ParseToml(files.Read(path));
            }
            catch
            {
                continue;
            }

            Collect(table, index);
        }

        return index;

        static void Collect(TomlTable table, Dictionary<string, TomlTable> into)
        {
            foreach (var (key, value) in table)
            {
                if (value is not TomlTable entry) continue;
                if (entry.ContainsKey("icon_sprite") || entry.ContainsKey("object") || entry.ContainsKey("name"))
                    into.TryAdd(key, entry);
                else
                    Collect(entry, into);
            }
        }
    }

    /// <summary>Points every sprite name a look replaces at the look's sprite.</summary>
    public static void RemapSprites(TomlTable table, FamilyLook look)
    {
        foreach (var key in table.Keys.ToList())
            table[key] = Remap(table[key], look);
    }

    private static object Remap(object value, FamilyLook look)
    {
        switch (value)
        {
            case string s:
                return look.Map.TryGetValue(s, out var mapped) ? mapped : s;
            case TomlTable t:
                RemapSprites(t, look);
                return t;
            case TomlArray a:
                for (var i = 0; i < a.Count; i++)
                    if (a[i] is { } item) a[i] = Remap(item, look);
                return a;
            case TomlTableArray ta:
                foreach (var t in ta) RemapSprites(t, look);
                return ta;
            default:
                return value;
        }
    }

    // Every shop list that sells an original also sells its variants, right after it, with the
    // same requirements. Random stock is left alone so variants never crowd out the rotation.
    private static void InsertIntoShops(IFileModifier files, string path, Dictionary<string, List<string>> variantsByBase)
    {
        if (variantsByBase.Count == 0 || !files.Exists(path)) return;
        TomlTable shops;
        try
        {
            shops = Toml.ParseToml(files.Read(path));
        }
        catch
        {
            return;
        }

        if (Insert(shops, variantsByBase)) files.Write(path, Toml.SerializeGameToml(shops));
    }

    private static bool Insert(TomlTable table, Dictionary<string, List<string>> variantsByBase)
    {
        var changed = false;
        foreach (var key in table.Keys.ToList())
        {
            switch (table[key])
            {
                case TomlTable child:
                    changed |= Insert(child, variantsByBase);
                    break;
                case TomlTableArray tables:
                    foreach (var child in tables) changed |= Insert(child, variantsByBase);
                    break;
                case TomlArray array when key != "random_stock":
                    changed |= InsertIntoArray(array, variantsByBase);
                    break;
            }
        }

        return changed;
    }

    private static bool InsertIntoArray(TomlArray array, Dictionary<string, List<string>> variantsByBase)
    {
        var rebuilt = new List<object?>();
        var changed = false;
        foreach (var entry in array)
        {
            rebuilt.Add(entry);
            var baseItem = entry switch
            {
                string s => s,
                TomlTable t => t.Get("item") as string,
                _ => null,
            };
            if (baseItem is null || !variantsByBase.TryGetValue(baseItem, out var variants)) continue;
            foreach (var variant in variants)
            {
                if (entry is TomlTable table)
                {
                    var clone = VariantPlanner.CloneTable(table);
                    clone["item"] = variant;
                    clone.Remove("include_recipe");
                    rebuilt.Add(clone);
                }
                else
                {
                    rebuilt.Add(variant);
                }

                changed = true;
            }
        }

        if (!changed) return false;
        array.Clear();
        foreach (var entry in rebuilt) array.Add(entry);
        return true;
    }

    // ── animals ──────────────────────────────────────────────────────────────

    private static void WriteAnimalVariants(IFileModifier files, VariantSession session, HashSet<string> staged,
        Action<string, string> report)
    {
        var all = session.AnimalVariants.Concat(session.RetiredAnimals).ToList();
        if (all.Count == 0) return;
        var written = 0;

        foreach (var byKind in all.GroupBy(a => a.Kind, StringComparer.Ordinal))
        {
            var path = InputSafety.AssetPath($"fiddle/ranching/animals/{byKind.Key}.toml");
            var animal = files.Exists(path) ? Toml.ParseToml(files.Read(path)) : null;
            if (animal?.Get("variants") is not TomlTable variants)
            {
                WarnRetiredGone(session, byKind, $"the animal kind '{byKind.Key}' is gone");
                continue;
            }

            var resolved = AnimalVariants.Read(animal).ToDictionary(v => v.Key, StringComparer.Ordinal);
            var changed = false;

            foreach (var planned in byKind)
            {
                var variant = planned;
                if (variants.ContainsKey(variant.VariantId)) continue;
                if (variants.Get(variant.BaseVariant) is not TomlTable baseTable ||
                    !resolved.TryGetValue(variant.BaseVariant, out var baseVariant))
                {
                    WarnRetiredGone(session, [variant], $"its original '{variant.BaseVariant}' is gone");
                    continue;
                }

                var baseName = baseTable.Get("name") as string ?? FamilyIndex.Prettify(variant.BaseVariant);
                var clone = NewClone(baseTable);

                if (!variant.Retired)
                {
                    var problem = ApplyLook(clone, variant, baseVariant, baseName, session, staged);
                    if (problem is not null)
                    {
                        session.Plan.Warnings.Add($"Animal variant '{variant.VariantId}' was not added: {problem}.");

                        // Barn animals of this variant may already live in saves; an unknown
                        // variant stops the save from loading, so keep it as a retired stand-in.
                        if (!session.History.Knows(variant)) continue;
                        variant = variant with { Retired = true };
                        clone = NewClone(baseTable);
                    }
                }

                if (variant.Retired)
                {
                    clone["acquirable"] = false;
                    clone["name"] = VariantFramework.VariantDisplayName(baseName, "retired look");
                }

                variants[variant.VariantId] = clone;
                session.WrittenAnimalVariants.Add(variant);
                changed = true;
                written++;
            }

            if (changed) files.Write(path, Toml.SerializeGameToml(animal));
        }

        if (written > 0) report($"Variants Framework: wrote {written} animal variant(s)", "");
    }

    // Born of nothing, never a starting unlock: only adoption (or an old save) brings one in.
    private static TomlTable NewClone(TomlTable baseTable)
    {
        var clone = VariantPlanner.CloneTable(baseTable);
        clone["born_in"] = new TomlArray();
        clone.Remove("default_unlocked");
        return clone;
    }

    /// <summary>Points a cloned variant at the look's sprite families; the reason when it cannot.</summary>
    private static string? ApplyLook(TomlTable clone, AnimalVariant variant, AnimalVariants.Variant baseVariant,
        string baseName, VariantSession session, HashSet<string> staged)
    {
        var look = session.Plan.FindLook(variant.FamilyId, variant.LookId);
        var family = session.Plan.RuntimeFamilies.FirstOrDefault(f => f.Family.Id == variant.FamilyId);
        if (look is null || family is null) return "its look is not part of this install";
        var types = AnimalVariants.ForLook(baseVariant, look, family.Family, session.Plan);
        if (types is null) return "its look no longer covers a complete animal";

        // Every sprite of a switched family must have landed, or the engine refuses the animal at
        // boot.
        var missing = look.Map.Values.FirstOrDefault(sprite =>
            types.Values.Any(t => sprite.StartsWith($"spr_animal_{variant.Kind}_{t.Key}_", StringComparison.Ordinal) ||
                                  (t.TopKey is not null && sprite.StartsWith($"spr_animal_{variant.Kind}_{t.TopKey}_", StringComparison.Ordinal)) ||
                                  sprite == $"spr_ui_icon_animal_{variant.Kind}_{t.UiKey}") &&
            !staged.Contains(sprite));
        if (missing is not null) return $"'{missing}' is not installed";

        clone["acquirable"] = true;
        clone["name"] = VariantFramework.VariantDisplayName(baseName,
            session.Plan.Looks.GetValueOrDefault(variant.LookId)?.Name ?? variant.LookId);
        foreach (var (type, entry) in types)
        {
            var typeTable = new TomlTable
            {
                ["key"] = entry.Key,
                ["top_key"] = entry.TopKey ?? "<n/a>",
                ["ui_key"] = entry.UiKey ?? entry.Key,
            };
            if (entry.Lut is not null) typeTable["lut"] = VariantPlanner.CloneValue(entry.Lut);
            if (entry.LutIndex is not null) typeTable["lut_index"] = VariantPlanner.CloneValue(entry.LutIndex);
            clone[type] = typeTable;
        }

        return null;
    }

    // A retired stand-in that cannot be built leaves its id unknown to the game; say so, because
    // a barn animal of that variant will not load.
    private static void WarnRetiredGone(VariantSession session, IEnumerable<AnimalVariant> variants, string reason)
    {
        foreach (var variant in variants.Where(v => v.Retired || session.History.Knows(v)))
            session.Plan.Warnings.Add($"Animal variant '{variant.VariantId}' could not be kept loadable: {reason}.");
    }
}
