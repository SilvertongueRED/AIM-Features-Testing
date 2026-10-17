# Seam: animal_sprites

Wraps `BaseAnimal.sprites_for_animation()` so every barn and coop animal's sprite set is filterable.

`animal_sprites` is a **template seam** (`op = "wrap"`). It feeds [animal.sprites](../hooks/animal.sprites.md). Mod authors never write seams. You register handlers for the hooks they dispatch. See [Seams](../SEAMS.md).

## Placement

| | |
| --- | --- |
| **File** | `gml/scripts/GameplaySystems/Ranching/Animal.gml` |
| **Locator** | whole-function wrap of `sprites_for_animation(animation, cardinality)` in the `BaseAnimal` constructor |
| **Op** | `wrap` |
| **Feeds** | [`animal.sprites`](../hooks/animal.sprites.md) |
| **Value filtered** | the sprite struct `sprites_for_animation()` returns |
| **ctx built** | `{ animal: self, animation: animation, cardinality: cardinality }` |
| **Marker** | `mmapi_animal_run_sprites_filters` |

## The Edit

A wrap targets the whole function: the pristine `sprites_for_animation` method is renamed `__mmapi_orig_sprites_for_animation`, its body untouched, and a generated wrapper takes its place (`blank_before` keeps a blank line between the two). The wrapper calls the renamed original and filters the returned struct through `mmapi_apply_filters("animal.sprites", <return>, { animal: self, animation: animation, cardinality: cardinality })` in the uniform try/catch shape.

`BaseAnimal` is the constructor every barn and coop animal, the mount, non-player animals and cutscene animals share, and `active_sprites()`, the instance's `set_sprites` and the UI portrait helpers all call `sprites_for_animation` by name, so the one wrap reaches all of them. ctx carries the requested `animation` and `cardinality`: the original's own fallbacks (an unknown animation becomes `"idle"`, a missing direction falls back East, South, North) happen inside it and show only in the returned sprites.

With zero handlers a wrap is behaviorally (not byte-) equivalent to pristine: the wrapper adds a call frame and an empty-registry early-out, nothing more. The mount resolves its sprites every frame, so that cost is paid per frame while mounted.

## See Also

- [animal.sprites](../hooks/animal.sprites.md) - This is the hook this seam dispatches.
- [pet_sprites](pet_sprites.md) - The same wrap on the pet's resolver.
- [animal_icon](animal_icon.md) - The wrap on the menu icon lookup.
- [animal_heart_points](animal_heart_points.md) - The other seam in `Animal.gml`.
