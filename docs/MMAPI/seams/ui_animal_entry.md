# Seam: ui_animal_entry

Hands each animal list row to mods once its icon is set.

`ui_animal_entry` is a **template seam** (`op = "emit"`). It feeds [ui.animal_entry](../hooks/ui.animal_entry.md). Mod authors never write seams. You register handlers for the hooks they dispatch. See [Seams](../SEAMS.md).

## Placement

| | |
| --- | --- |
| **File** | `gml/scripts/AnimalMenu.gml` |
| **Locator** | pristine context in the `AnimalEntry` constructor, between the close of the icon `if`/`else` (pet prototype icon or `get_animal_icon_data`) and the first method, `shift_basic_details_up()` |
| **Op** | `emit` |
| **Feeds** | [`ui.animal_entry`](../hooks/ui.animal_entry.md) |
| **ctx built** | `{ entry: self, animal: self.animal, icon: self.animal_icon }` |
| **Marker** | `mmapi_ui_run_animal_entry` |

## The Edit

The `AnimalEntry(animal, scroller, pilot, element_height)` constructor builds one list row: a scroller element with the animal's name label, then `animal_icon`, a sprite node set either from the pet prototype (for the global `PET`) or from [`get_animal_icon_data`](animal_icon.md) (for every other animal), with its LUT applied. The generated emit lands right after that `if`/`else`, calling `mmapi_emit("ui.animal_entry", { entry: self, animal: self.animal, icon: self.animal_icon })` in the uniform try/catch shape.

Everything the constructor itself builds exists at the emit. The row's optional details (sex icon, hearts, petting, food, pregnancy, currency) are methods declared below the emit point that callers chain on after `new AnimalEntry(...)` returns, so they are not there yet. The constructor runs for the animal journal list (mount, pet and barn animals), the daycare, sell and festival pickers, and both adoption lists, once per row per menu build.

With zero handlers the emit early-outs on an empty registry, leaving pristine behavior.

## See Also

- [ui.animal_entry](../hooks/ui.animal_entry.md) - This is the hook this seam dispatches.
- [animal_icon](animal_icon.md) - The icon lookup the constructor calls just before this emit.
- [ui_relationship_row_built](ui_relationship_row_built.md) - The same per-row emit for the NPC relationships journal.
- [pet_appearance_popup_scrollable](pet_appearance_popup_scrollable.md) - The engine fix in the same file.
