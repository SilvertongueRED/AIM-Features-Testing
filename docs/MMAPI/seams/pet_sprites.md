# Seam: pet_sprites

Wraps `Pet.sprites_for_animation()` so the farm pet's sprite set is filterable.

`pet_sprites` is a **template seam** (`op = "wrap"`). It feeds [pet.sprites](../hooks/pet.sprites.md). Mod authors never write seams. You register handlers for the hooks they dispatch. See [Seams](../SEAMS.md).

## Placement

| | |
| --- | --- |
| **File** | `gml/scripts/Pet.gml` |
| **Locator** | whole-function wrap of `sprites_for_animation(animation, cardinality)` in the `Pet` constructor |
| **Op** | `wrap` |
| **Feeds** | [`pet.sprites`](../hooks/pet.sprites.md) |
| **Value filtered** | the sprite struct `sprites_for_animation()` returns |
| **ctx built** | `{ pet: self, animation: animation, cardinality: cardinality }` |
| **Marker** | `mmapi_pet_run_sprites_filters` |

## The Edit

A wrap targets the whole function: the pristine `sprites_for_animation` method is renamed `__mmapi_orig_sprites_for_animation`, its body untouched, and a generated wrapper takes its place (`blank_before` keeps a blank line between the two). The wrapper calls the renamed original and filters the returned struct through `mmapi_apply_filters("pet.sprites", <return>, { pet: self, animation: animation, cardinality: cardinality })` in the uniform try/catch shape.

The pet's `active_sprites()`, its world instance and the journal portrait all resolve through this method by name, so the one wrap covers the pet everywhere it renders. The pet resolves on state changes, not per frame.

With zero handlers a wrap is behaviorally (not byte-) equivalent to pristine: the wrapper adds a call frame and an empty-registry early-out, nothing more.

## See Also

- [pet.sprites](../hooks/pet.sprites.md) - This is the hook this seam dispatches.
- [animal_sprites](animal_sprites.md) - The same wrap on the barn and coop animals' resolver.
- [pet_created](pet_created.md) - The pet spawn emit in the same file.
