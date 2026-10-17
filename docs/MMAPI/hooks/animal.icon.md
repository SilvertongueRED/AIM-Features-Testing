# Hook: animal.icon

Swap the menu icon a barn animal kind or variant shows.

`animal.icon` is a **filter** hook. Register a callback with `mmapi_filter`. See [Hooks](../HOOKS.md) for how registration and dispatch work.

## Contract

Wraps `get_animal_icon_data(kind, variant, sex, adult)`, the lookup behind every barn animal icon in the animal journal list, the mount picker, the adoption menu header and the farm building selection menu. The filtered value is the struct the engine returns. ctx is `{ kind, variant, sex, adult }` exactly as passed. Return the struct (mutated or replaced), or `undefined` to keep it.

| | |
| --- | --- |
| **Fires** | Every time `get_animal_icon_data()` resolves, after the engine has built its answer. |
| **Value** | `{ sprite, lut, lut_index }` |
| **ctx** | `{ kind, variant, sex, adult }` |
| **Kind contract** | The callback receives the current value and returns a replacement, or `undefined` to keep the current value. |

### The value struct

- `sprite` - the icon sprite (the variant's `ui_icon` for that sex, or its baby icon).
- `lut`, `lut_index` - the colour palette the icon draws with, or `undefined`.

### The ctx struct

- `kind` - the `AnimalKind`.
- `variant` - the variant asked for. It may be `undefined`, meaning the kind's `default_icon_variant` (the adoption menu header asks this way).
- `sex` - the `Sex` asked for. Callers that omit it get the function's default, `Sex.Female`.
- `adult` - `true` for the adult icon, `false` for the baby icon. Defaults to `true`.

> [!NOTE]
> This site does not know which individual animal is drawn: two cows of the same variant and sex ask for the same icon. [ui.animal_entry](ui.animal_entry.md) fires afterwards for the list rows that do know their animal, so restyle a single animal's row there.

## Usage

```gml
// animal.icon is a FILTER: you receive (value, ctx) and return a
// replacement, or undefined to keep the game's value.
function fresh_coat_animal_icon(_value, _ctx) {
    if (_value == undefined) return undefined; // test undefined BEFORE anything else
    // _ctx is { kind, variant, sex, adult }; variant may be undefined.
    var _icon = fresh_coat_icon_for(_ctx.kind, _ctx.variant, _ctx.adult); // your own lookup
    if (_icon == undefined) return undefined;
    _value.sprite = _icon;
    return _value;
}

// inside your latched register function (see Mod Anatomy):
mmapi_filter("animal.icon", fresh_coat_animal_icon);
```

The engine builds a fresh struct on every call, so mutating `_value` and returning it is safe.

## Engine Wiring

- Seam [`animal_icon`](../seams/animal_icon.md) dispatches from `gml/scripts/GameplaySystems/Ranching/AnimalUtils.gml`, a whole-function wrap of `get_animal_icon_data()` that filters its return value.

## See Also

- [ui.animal_entry](ui.animal_entry.md) - Restyle one animal's row, after this lookup has run.
- [animal.sprites](animal.sprites.md) - Swap the world sprites a barn or coop animal renders with.
- [animal.adoption_variant_unlocked](animal.adoption_variant_unlocked.md) - Change whether an adoption variant is available.
