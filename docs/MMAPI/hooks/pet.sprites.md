# Hook: pet.sprites

Swap the sprites the farm pet renders with.

`pet.sprites` is a **filter** hook. Register a callback with `mmapi_filter`. See [Hooks](../HOOKS.md) for how registration and dispatch work.

## Contract

Wraps `Pet.sprites_for_animation(animation, cardinality)`, the resolver for the player's pet in the world and in the journal portrait. The filtered value is the struct the engine returns. ctx is `{ pet, animation, cardinality }`. Return the struct (mutated or replaced), or `undefined` to keep it. Called on state changes, not per frame.

| | |
| --- | --- |
| **Fires** | Every time `Pet.sprites_for_animation()` resolves, after the engine has built its answer. |
| **Value** | `{ lut_index, shadow, base_sprite, base_cosmetic, lut, lut_texture, lut_uvs, top_sprite, top_cosmetic }` |
| **ctx** | `{ pet, animation, cardinality }` |
| **Kind contract** | The callback receives the current value and returns a replacement, or `undefined` to keep the current value. |

### The value struct

- `base_sprite` - the pet's sprite for this animation and direction.
- `base_cosmetic` - the worn cosmetic's sprite, or `undefined`.
- `shadow` - the shadow sprite, looked up from `base_sprite` in `SHADOW_DICTIONARY`.
- `lut`, `lut_texture`, `lut_index`, `lut_uvs` - the colour palette the variant draws with.
- `top_sprite` / `top_cosmetic` - always `undefined` for pets; present so the struct has the same shape as an animal's.

### The ctx struct

- `pet` - the `Pet` struct (the global `PET`): `pet.variant` is the chosen appearance, `pet.pet_kind()` the kind (cat, dog, ...).
- `animation` - the requested animation name.
- `cardinality` - the requested direction. The engine falls back East, South, North when the variant has no sprite for it.

> [!IMPORTANT]
> When you change `base_sprite`, also set `shadow = SHADOW_DICTIONARY.get(<new base_sprite>)`, or the pet keeps casting the old sprite's shadow. Keep frame counts and origins compatible with the vanilla animation.

## Usage

```gml
// pet.sprites is a FILTER: you receive (value, ctx) and return a
// replacement, or undefined to keep the game's value.
function fresh_coat_pet_sprites(_value, _ctx) {
    if (_value == undefined) return undefined; // test undefined BEFORE anything else
    // _ctx is { pet, animation, cardinality }.
    var _sprite = fresh_coat_pet_sprite_for(_ctx.pet.variant, _value.base_sprite); // your own lookup
    if (_sprite == undefined) return undefined;
    _value.base_sprite = _sprite;
    _value.shadow = SHADOW_DICTIONARY.get(_sprite); // keep the shadow in step
    return _value;
}

// inside your latched register function (see Mod Anatomy):
mmapi_filter("pet.sprites", fresh_coat_pet_sprites);
```

The engine builds a fresh struct on every call, so mutating `_value` and returning it is safe.

## Interactions

- The pet's menu icon is not resolved here. Its journal row is restyled through [ui.animal_entry](ui.animal_entry.md), where `ctx.animal` is the global `PET`.
- A changed decision applies the next time the pet resolves its sprites (its next state change), not instantly on a pet already on screen.

## Engine Wiring

- Seam [`pet_sprites`](../seams/pet_sprites.md) dispatches from `gml/scripts/Pet.gml`, a whole-function wrap of `Pet.sprites_for_animation()` that filters its return value.

## See Also

- [animal.sprites](animal.sprites.md) - The same resolver hook for barn and coop animals.
- [pet.created](pet.created.md) - Customize the farm pet instance as it spawns.
- [ui.animal_entry](ui.animal_entry.md) - Restyle each animal row, the pet's included.
