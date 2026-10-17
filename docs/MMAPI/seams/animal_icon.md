# Seam: animal_icon

Wraps `get_animal_icon_data()` so every barn animal menu icon is filterable.

`animal_icon` is a **template seam** (`op = "wrap"`). It feeds [animal.icon](../hooks/animal.icon.md). Mod authors never write seams. You register handlers for the hooks they dispatch. See [Seams](../SEAMS.md).

## Placement

| | |
| --- | --- |
| **File** | `gml/scripts/GameplaySystems/Ranching/AnimalUtils.gml` |
| **Locator** | whole-function wrap of `get_animal_icon_data(kind, variant, sex=Sex.Female, adult=true)` |
| **Op** | `wrap` |
| **Feeds** | [`animal.icon`](../hooks/animal.icon.md) |
| **Value filtered** | the `{ sprite, lut, lut_index }` struct `get_animal_icon_data()` returns |
| **ctx built** | `{ kind: kind, variant: variant, sex: sex, adult: adult }` |
| **Marker** | `mmapi_animal_run_icon_filters` |

## The Edit

A wrap targets the whole function: the pristine global `get_animal_icon_data` is renamed `__mmapi_orig_get_animal_icon_data`, its body untouched, and a generated wrapper with the same signature and defaults takes its place (`blank_before` keeps a blank line between the two). The wrapper calls the renamed original and filters the returned struct through `mmapi_apply_filters("animal.icon", <return>, { kind: kind, variant: variant, sex: sex, adult: adult })` in the uniform try/catch shape.

The ctx carries the wrapper's own parameters, so `variant` is `undefined` when the caller passed none (the original resolves the kind's `default_icon_variant` internally), while `sex` and `adult` carry their declared defaults when omitted. Every icon site calls the function by name: the `AnimalEntry` row constructor, the mount picker, the adoption menu header and the farm building selection menu.

With zero handlers a wrap is behaviorally (not byte-) equivalent to pristine: the wrapper adds a call frame and an empty-registry early-out, nothing more.

## See Also

- [animal.icon](../hooks/animal.icon.md) - This is the hook this seam dispatches.
- [ui_animal_entry](ui_animal_entry.md) - The per-row emit that follows this lookup in the `AnimalEntry` constructor.
- [animal_created](animal_created.md) - The other seam in `AnimalUtils.gml`.
