using System.Text.Json;
using Tomlyn.Model;

namespace Garethp.ModsOfMistriaInstallerLib.Variants;

public enum FamilyKind
{
    /// <summary>A furniture object (rugs, chests, lamps, décor) plus the icons of the items
    /// that place it. Per placed object, global, and variant items.</summary>
    Furniture,
    /// <summary>A barn or coop animal kind. Per animal, global, and adoption variants.</summary>
    Animal,
    /// <summary>A pet kind (cat, dog, ...). One pet per save, switched directly.</summary>
    Pet,
    /// <summary>A farm building (barns, coops, greenhouse...). Per building and global.</summary>
    Building,
    /// <summary>A world object such as a tree, crop, bush or rock. Global only.</summary>
    WorldObject,
    /// <summary>An item's inventory icon (non-furniture). Global only.</summary>
    Item,
    /// <summary>Anything else two or more mods replace: portraits, interface, scenery.
    /// Chosen in game but applied by the next install, because the engine cannot swap these
    /// at runtime.</summary>
    InstallOnly
}

public sealed class Family
{
    public required string Id { get; init; }
    public required FamilyKind Kind { get; init; }
    public required string DisplayName { get; set; }

    /// <summary>The engine key: object id, item id, animal kind, pet kind or building id.</summary>
    public string Key { get; init; } = "";

    /// <summary>Items that place this furniture object (variant items are cloned from these).</summary>
    public List<string> ItemIds { get; } = [];

    /// <summary>Every vanilla sprite this family owns, in a stable order.</summary>
    public List<string> Sprites { get; } = [];

    public HashSet<string> SpriteSet { get; } = new(StringComparer.Ordinal);

    /// <summary>The sprite the picker previews (the world sprite, or the icon).</summary>
    public string? PreviewSprite { get; set; }

    public string? IconSprite { get; set; }

    /// <summary>Closed door sprites (a building's <c>door_closed</c>): the engine derives their
    /// swing animation by name, so looks must keep those names paired.</summary>
    public HashSet<string> DoorSprites { get; } = new(StringComparer.Ordinal);

    public void Add(string sprite)
    {
        if (SpriteSet.Add(sprite)) Sprites.Add(sprite);
    }

    public bool IsRuntime => Kind != FamilyKind.InstallOnly;
}

/// <summary>
/// Maps vanilla sprites to the game entity that owns them, built from the pristine fiddle data.
/// A sprite belongs to at most one family; earlier categories win (furniture, animals, pets,
/// buildings, world objects, item icons).
/// </summary>
public sealed class FamilyIndex
{
    private readonly Dictionary<string, Family> _families = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Family> _bySprite = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, Family> Families => _families;

    public Family? FamilyOf(string sprite) => _bySprite.GetValueOrDefault(sprite);

    /// <summary>Animal kind (e.g. "chicken") to its known animation names, longest first.</summary>
    public Dictionary<string, List<string>> AnimalAnimations { get; } = new(StringComparer.Ordinal);

    /// <summary>Item tables as they appear in the pristine data, by id (for names).</summary>
    public Dictionary<string, string> ItemNames { get; } = new(StringComparer.Ordinal);

    /// <summary>Shadow manifest of the pristine game: sprite to its shadow sprite.</summary>
    public Dictionary<string, string> Shadows { get; } = new(StringComparer.Ordinal);

    /// <summary>Outline manifest of the pristine game: icon to its outline sprite.</summary>
    public Dictionary<string, string> Outlines { get; } = new(StringComparer.Ordinal);

    public static FamilyIndex Build(PristineAssets pristine)
    {
        var index = new FamilyIndex();
        index.LoadManifests(pristine);

        var items = ReadItems(pristine);
        foreach (var item in items) index.ItemNames[item.Id] = item.Name ?? item.Id;

        index.AddFurniture(pristine, items);
        index.AddAnimals(pristine);
        index.AddPets(pristine);
        index.AddPrototypeFile(pristine, "assets/fiddle/object_prototypes/building.toml", FamilyKind.Building, "building");
        foreach (var world in new[] { "tree", "crop", "bush", "rock", "stump", "breakable", "dig_site", "grass" })
            index.AddPrototypeFile(pristine, $"assets/fiddle/object_prototypes/{world}.toml", FamilyKind.WorldObject, "world");
        index.AddItemIcons(items);
        index.AttachDependents();

        // Families without a single real sprite are noise.
        foreach (var id in index._families.Where(kv => kv.Value.Sprites.Count == 0).Select(kv => kv.Key).ToList())
            index._families.Remove(id);
        return index;
    }

    private void LoadManifests(PristineAssets pristine)
    {
        ReadJsonMap(pristine.ReadText("assets/data_files/animation/shadow_manifest.json"), Shadows);
        ReadJsonMap(pristine.ReadText("assets/data_files/animation/outlines.json"), Outlines);
    }

    private static void ReadJsonMap(string? text, Dictionary<string, string> into)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
            foreach (var property in doc.RootElement.EnumerateObject())
                if (property.Value.ValueKind == JsonValueKind.String)
                    into[property.Name] = property.Value.GetString()!;
        }
        catch (JsonException)
        {
            // A malformed manifest only costs shadow/outline linking, never the install.
        }
    }

    // ── furniture ────────────────────────────────────────────────────────────

    private void AddFurniture(PristineAssets pristine, List<ItemInfo> items)
    {
        var text = pristine.ReadText("assets/fiddle/object_prototypes/furniture.toml");
        if (text is null) return;
        var table = ParseQuiet(text);
        var itemsByObject = items.Where(i => i.Object is not null)
            .GroupBy(i => i.Object!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var (objectId, value) in table)
        {
            if (value is not TomlTable proto) continue;
            if (objectId == "default") continue; // the template every piece inherits from
            var family = new Family
            {
                Id = "furniture:" + objectId,
                Kind = FamilyKind.Furniture,
                Key = objectId,
                DisplayName = Prettify(objectId),
            };

            family.PreviewSprite = MainSprite(proto);
            foreach (var sprite in SpriteStrings(proto)) Claim(family, sprite, pristine);

            if (itemsByObject.TryGetValue(objectId, out var placing))
            {
                var first = true;
                foreach (var item in placing)
                {
                    family.ItemIds.Add(item.Id);
                    if (first && !string.IsNullOrEmpty(item.Name)) family.DisplayName = item.Name!;
                    if (first) family.IconSprite = item.IconSprite;
                    first = false;
                    foreach (var sprite in item.Sprites) Claim(family, sprite, pristine);
                }
            }

            if (family.PreviewSprite is null || !family.SpriteSet.Contains(family.PreviewSprite))
                family.PreviewSprite = family.Sprites.FirstOrDefault();
            _families[family.Id] = family;
        }
    }

    // The south (or first available) world sprite: what the player sees most.
    private static string? MainSprite(TomlTable proto)
    {
        foreach (var direction in new[] { "south", "east", "north", "west" })
        {
            if (proto.TryGetValue(direction, out var d) && d is TomlTable cardinal &&
                cardinal.TryGetValue("sprite", out var s) && s is string sprite &&
                sprite.StartsWith("spr_", StringComparison.Ordinal))
                return sprite;
        }

        return null;
    }

    // ── animals ──────────────────────────────────────────────────────────────

    private void AddAnimals(PristineAssets pristine)
    {
        var defaultAnimations = new List<string>();
        var defaultText = pristine.ReadText("assets/fiddle/ranching/animals/default.toml");
        if (defaultText is not null) defaultAnimations.AddRange(AnimationNames(ParseQuiet(defaultText)));

        var spritesByPrefix = pristine.SpriteNames.Order(StringComparer.Ordinal).ToList();
        foreach (var path in pristine.PathsUnder("assets/fiddle/ranching/animals/"))
        {
            if (!path.EndsWith(".toml", StringComparison.Ordinal)) continue;
            var kind = Path.GetFileNameWithoutExtension(path);
            if (kind == "default") continue;
            var table = ParseQuiet(pristine.ReadText(path) ?? "");

            var name = Prettify(kind);
            if (table.TryGetValue("core", out var c) && c is TomlTable core &&
                core.TryGetValue("name", out var n) && n is string animalName)
                name = animalName;

            var animations = defaultAnimations.Concat(AnimationNames(table)).Distinct(StringComparer.Ordinal)
                .OrderByDescending(a => a.Length).ToList();
            AnimalAnimations[kind] = animations;

            var family = new Family
            {
                Id = "animal:" + kind,
                Kind = FamilyKind.Animal,
                Key = kind,
                DisplayName = name,
            };

            var bodyPrefix = $"spr_animal_{kind}_";
            var iconPrefix = $"spr_ui_icon_animal_{kind}_";
            foreach (var sprite in spritesByPrefix)
            {
                if (!(sprite.StartsWith(bodyPrefix, StringComparison.Ordinal) ||
                      sprite.StartsWith(iconPrefix, StringComparison.Ordinal))) continue;
                if (IsLut(sprite)) continue;
                Claim(family, sprite, pristine);
            }

            family.PreviewSprite = PreferredPreview(family, kind);
            _families[family.Id] = family;
        }
    }

    private static IEnumerable<string> AnimationNames(TomlTable table)
    {
        if (table.TryGetValue("animations", out var a) && a is TomlTable animations)
            foreach (var key in animations.Keys) yield return key;
    }

    private static string? PreferredPreview(Family family, string kind)
    {
        foreach (var candidate in new[]
                 {
                     $"spr_animal_{kind}_basic_female_idle_east", $"spr_animal_{kind}_basic_male_idle_east",
                     $"spr_animal_{kind}_basic_idle_east", $"spr_animal_{kind}_basic_base_idle_east",
                 })
        {
            if (family.SpriteSet.Contains(candidate)) return candidate;
        }

        return family.Sprites.FirstOrDefault(s => s.Contains("_idle_", StringComparison.Ordinal) &&
                                                  !s.Contains("baby", StringComparison.Ordinal) &&
                                                  !s.Contains("cosmetic", StringComparison.Ordinal))
               ?? family.Sprites.FirstOrDefault();
    }

    // ── pets ─────────────────────────────────────────────────────────────────

    private void AddPets(PristineAssets pristine)
    {
        var text = pristine.ReadText("assets/fiddle/pets.toml");
        if (text is null) return;
        var table = ParseQuiet(text);
        if (!table.TryGetValue("variants", out var v) || v is not TomlTable variants) return;

        var byKind = new Dictionary<string, Family>(StringComparer.Ordinal);
        var allSprites = pristine.SpriteNames.Order(StringComparer.Ordinal).ToList();
        foreach (var (variantKey, value) in variants)
        {
            if (value is not TomlTable variant) continue;
            var kind = variant.TryGetValue("pet_kind", out var k) && k is string petKind ? petKind : variantKey;
            if (!byKind.TryGetValue(kind, out var family))
            {
                family = new Family
                {
                    Id = "pet:" + kind,
                    Kind = FamilyKind.Pet,
                    Key = kind,
                    DisplayName = Prettify(kind),
                };
                byKind[kind] = family;
            }

            if (variant.TryGetValue("sprite_template", out var t) && t is string template)
            {
                var marker = template.IndexOf("{ANIMATION_KEY}", StringComparison.Ordinal);
                var prefix = marker < 0 ? template : template[..marker];
                foreach (var sprite in allSprites.Where(s => s.StartsWith(prefix, StringComparison.Ordinal)))
                    if (!IsLut(sprite)) Claim(family, sprite, pristine);
            }

            foreach (var key in new[] { "ui_icon", "map_icon" })
                if (variant.TryGetValue(key, out var icon) && icon is string iconSprite)
                    Claim(family, iconSprite, pristine);
        }

        foreach (var family in byKind.Values)
        {
            family.PreviewSprite = family.Sprites.FirstOrDefault(s => s.Contains("_idle_east", StringComparison.Ordinal))
                                   ?? family.Sprites.FirstOrDefault(s => s.Contains("_idle_", StringComparison.Ordinal))
                                   ?? family.Sprites.FirstOrDefault();
            _families[family.Id] = family;
        }
    }

    // ── buildings and world objects ──────────────────────────────────────────

    private void AddPrototypeFile(PristineAssets pristine, string path, FamilyKind kind, string prefix)
    {
        var text = pristine.ReadText(path);
        if (text is null) return;
        foreach (var (objectId, value) in ParseQuiet(text))
        {
            if (value is not TomlTable proto) continue;
            // [default] is the template every entry of the file inherits from, not an object:
            // its art is shared by all of them, so it is left to the install-time overlaps.
            if (objectId == "default") continue;
            var family = new Family
            {
                Id = $"{prefix}:{objectId}",
                Kind = kind,
                Key = objectId,
                DisplayName = proto.TryGetValue("name", out var n) && n is string name ? name : Prettify(objectId),
            };
            foreach (var sprite in SpriteStrings(proto)) Claim(family, sprite, pristine);
            // A door's opening animation is not listed: obj_door finds it by swapping the first
            // "_closed" in the closed sprite's name for "_swing". It is still the building's art.
            if (proto.Get("door_closed") is TomlArray doors)
            {
                foreach (var door in SpriteStrings(doors))
                {
                    if (!family.SpriteSet.Contains(door)) continue;
                    family.DoorSprites.Add(door);
                    var swing = VariantPlanner.DoorSwingName(door);
                    if (swing is not null) Claim(family, swing, pristine);
                }
            }
            // Buildings list their art per season under [sprites]; the first spring one is the
            // building itself (not its door or name plate).
            var main = proto.Get("sprites") is TomlTable seasons ? SpriteStrings(seasons.Get("spring")).FirstOrDefault() : null;
            family.PreviewSprite = main is not null && family.SpriteSet.Contains(main)
                ? main
                : family.Sprites.FirstOrDefault(s => s.Contains("spring", StringComparison.Ordinal) &&
                                                     !s.Contains("door", StringComparison.Ordinal) &&
                                                     !s.Contains("plate", StringComparison.Ordinal))
                  ?? family.Sprites.FirstOrDefault();
            if (family.Sprites.Count > 0) _families.TryAdd(family.Id, family);
        }
    }

    // ── items ────────────────────────────────────────────────────────────────

    private void AddItemIcons(List<ItemInfo> items)
    {
        foreach (var item in items)
        {
            if (item.Sprites.Count == 0) continue;
            if (item.IconSprite is not null && _bySprite.ContainsKey(item.IconSprite)) continue;
            var family = new Family
            {
                Id = "item:" + item.Id,
                Kind = FamilyKind.Item,
                Key = item.Id,
                DisplayName = item.Name ?? Prettify(item.Id),
                IconSprite = item.IconSprite,
                PreviewSprite = item.IconSprite,
            };
            family.ItemIds.Add(item.Id);
            foreach (var sprite in item.Sprites)
            {
                if (_bySprite.ContainsKey(sprite)) continue;
                family.Add(sprite);
                _bySprite[sprite] = family;
            }

            if (family.Sprites.Count > 0) _families.TryAdd(family.Id, family);
        }
    }

    // Shadows and outlines follow the sprite they belong to, so a recoloured lamp keeps a
    // shadow and a recoloured icon keeps its hover outline.
    private void AttachDependents()
    {
        foreach (var family in _families.Values.ToList())
        {
            foreach (var sprite in family.Sprites.ToList())
            {
                if (Shadows.TryGetValue(sprite, out var shadow) && !_bySprite.ContainsKey(shadow))
                {
                    family.Add(shadow);
                    _bySprite[shadow] = family;
                }

                if (Outlines.TryGetValue(sprite, out var outline) && !_bySprite.ContainsKey(outline))
                {
                    family.Add(outline);
                    _bySprite[outline] = family;
                }
            }
        }
    }

    private void Claim(Family family, string sprite, PristineAssets pristine)
    {
        if (!pristine.HasSprite(sprite)) return;
        if (_bySprite.TryGetValue(sprite, out var owner) && owner != family) return;
        family.Add(sprite);
        _bySprite[sprite] = family;
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    public sealed record ItemInfo(string Id, string? Name, string? Object, string? IconSprite, List<string> Sprites);

    public static List<ItemInfo> ReadItems(PristineAssets pristine)
    {
        var output = new List<ItemInfo>();
        foreach (var path in pristine.PathsUnder("assets/fiddle/items/"))
        {
            if (!path.EndsWith(".toml", StringComparison.Ordinal) ||
                path.EndsWith(".meta.toml", StringComparison.Ordinal)) continue;
            CollectItems(ParseQuiet(pristine.ReadText(path) ?? ""), output);
        }

        return output;
    }

    private static readonly string[] ItemSpriteKeys = ["icon_sprite", "mini_sprite", "sow_sprite", "crafting_preview_sprite"];

    private static void CollectItems(TomlTable table, List<ItemInfo> output)
    {
        foreach (var (key, value) in table)
        {
            if (value is not TomlTable entry) continue;
            if (entry.ContainsKey("icon_sprite") || entry.ContainsKey("object") || entry.ContainsKey("name"))
            {
                var sprites = new List<string>();
                foreach (var spriteKey in ItemSpriteKeys)
                    if (entry.TryGetValue(spriteKey, out var s) && s is string sprite) sprites.Add(sprite);
                if (entry.TryGetValue("blueprint", out var b) && b is TomlTable blueprint &&
                    blueprint.TryGetValue("preview_sprite", out var p) && p is string preview)
                    sprites.Add(preview);

                output.Add(new ItemInfo(
                    key,
                    entry.TryGetValue("name", out var n) ? n as string : null,
                    entry.TryGetValue("object", out var o) ? o as string : null,
                    entry.TryGetValue("icon_sprite", out var i) ? i as string : null,
                    sprites));
                continue;
            }

            CollectItems(entry, output);
        }
    }

    /// <summary>Every <c>spr_*</c> string anywhere inside a TOML value.</summary>
    public static IEnumerable<string> SpriteStrings(object? value)
    {
        switch (value)
        {
            case string s when s.StartsWith("spr_", StringComparison.Ordinal):
                yield return s;
                break;
            case TomlTable table:
                foreach (var (_, child) in table)
                foreach (var s in SpriteStrings(child))
                    yield return s;
                break;
            case TomlArray array:
                foreach (var child in array)
                foreach (var s in SpriteStrings(child))
                    yield return s;
                break;
            case TomlTableArray tables:
                foreach (var child in tables)
                foreach (var s in SpriteStrings(child))
                    yield return s;
                break;
        }
    }

    public static bool IsLut(string sprite) =>
        sprite.EndsWith("_lut", StringComparison.Ordinal) || sprite.Contains("_lut_", StringComparison.Ordinal);

    public static TomlTable ParseQuiet(string text)
    {
        try
        {
            return Utils.Toml.ParseToml(text);
        }
        catch
        {
            return new TomlTable();
        }
    }

    public static string Prettify(string id)
    {
        var words = id.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..]);
        return string.Join(' ', words);
    }
}
