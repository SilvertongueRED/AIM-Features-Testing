# Hook: animal.sprites

Swap the sprites a barn or coop animal renders with, per animal.

`animal.sprites` is a **filter** hook. Register a callback with `mmapi_filter`. See [Hooks](../HOOKS.md) for how registration and dispatch work.

## Contract

Wraps `BaseAnimal.sprites_for_animation(animation, cardinality)`, the one resolver every barn and coop animal, the mount, non-player animals and cutscene animals use for world sprites, held animals, the journal portrait and naming popups. The filtered value is the struct the engine returns. ctx is `{ animal, animation, cardinality }`. Return the struct (mutated or replaced), or `undefined` to keep it.

| | |
| --- | --- |
| **Fires** | Every time `BaseAnimal.sprites_for_animation()` resolves, after the engine has built its answer. |
| **Value** | `{ base_sprite, top_sprite, base_cosmetic, top_cosmetic, shadow, lut, lut_texture, lut_index, lut_uvs }` |
| **ctx** | `{ animal, animation, cardinality }` |
| **Kind contract** | The callback receives the current value and returns a replacement, or `undefined` to keep the current value. |

### The value struct

- `base_sprite` - the animal's body sprite for this animation and direction.
- `top_sprite` - the layer drawn over the body (wool, plumage), or `undefined`.
- `base_cosmetic` / `top_cosmetic` - the worn cosmetic's sprites, or `undefined`.
- `shadow` - the shadow sprite, looked up from `base_sprite` in `SHADOW_DICTIONARY`.
- `lut`, `lut_texture`, `lut_index`, `lut_uvs` - the colour palette the variant draws with.

### The ctx struct

- `animal` - the animal data struct: `animal.kind`, `animal.variant`, `animal.sex`, `animal.idx` (the stable per-animal id), and `animal.is_baby()`.
- `animation` - the requested animation name, as asked for. An animation the variant lacks is resolved as `"idle"` inside the engine, so the value can show idle sprites for a different request.
- `cardinality` - the requested direction. The engine falls back East, South, North when the variant has no sprite for it.

> [!IMPORTANT]
> When you change `base_sprite`, also set `shadow = SHADOW_DICTIONARY.get(<new base_sprite>)`, or the animal keeps casting the old sprite's shadow (or none). Keep frame counts and origins compatible with the vanilla animation: gameplay reads `image_index` ranges, so a strip with fewer frames cuts animations short or stalls them.

> [!WARNING]
> Not per frame for stabled animals (`set_sprites` runs on state changes), but **the mount calls it every frame**. Keep the handler's first check cheap, and return `undefined` at once for animals you do not restyle.

## Usage

```gml
// animal.sprites is a FILTER: you receive (value, ctx) and return a
// replacement, or undefined to keep the game's value.
function fresh_coat_animal_sprites(_value, _ctx) {
    if (_value == undefined) return undefined; // test undefined BEFORE anything else
    // _ctx is { animal, animation, cardinality }.
    var _look = fresh_coat_look_for(_ctx.animal.idx); // your own per-animal state
    if (_look == undefined) return undefined; // cheap exit: the mount calls this every frame

    var _sprite = fresh_coat_sprite_for(_look, _value.base_sprite);
    if (_sprite == undefined) return undefined;
    _value.base_sprite = _sprite;
    _value.shadow = SHADOW_DICTIONARY.get(_sprite); // keep the shadow in step
    return _value;
}

// inside your latched register function (see Mod Anatomy):
mmapi_filter("animal.sprites", fresh_coat_animal_sprites);
```

The engine builds a fresh struct on every call, so mutating `_value` and returning it is safe. `fresh_coat_look_for` and `fresh_coat_sprite_for` stand for your own lookups.

## Interactions

- Menu icons do not go through this resolver. Restyle them with [animal.icon](animal.icon.md) (per kind and variant) and [ui.animal_entry](ui.animal_entry.md) (per listed animal).
- The farm pet has its own resolver and hook, [pet.sprites](pet.sprites.md).

## Engine Wiring

- Seam [`animal_sprites`](../seams/animal_sprites.md) dispatches from `gml/scripts/GameplaySystems/Ranching/Animal.gml`, a whole-function wrap of `BaseAnimal.sprites_for_animation()` that filters its return value.

## See Also

- [pet.sprites](pet.sprites.md) - The same resolver hook for the farm pet.
- [animal.icon](animal.icon.md) - Swap the menu icon a barn animal kind or variant shows.
- [ui.animal_entry](ui.animal_entry.md) - Restyle each animal row in the journal and animal pickers.
- [animal.created](animal.created.md) - Customize each barn/coop animal instance as it spawns.
