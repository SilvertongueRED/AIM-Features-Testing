# Hook: ui.animal_entry

Restyle each animal row in the journal and the animal pickers.

`ui.animal_entry` is an **event** hook. Register a callback with `mmapi_on`. See [Hooks](../HOOKS.md) for how registration and dispatch work.

## Contract

Fires at the end of the `AnimalEntry` constructor, the list row the animal journal, daycare, sell, adoption and festival pickers build for one animal or the pet, after its icon sprite and LUT are set. ctx is `{ entry, animal, icon }`. The return value is ignored.

| | |
| --- | --- |
| **Fires** | In the `AnimalEntry` constructor, once the row's label and icon are set, once per row per menu build. |
| **ctx** | `{ entry, animal, icon }` |
| **Kind contract** | The callback observes the moment. Its return value is ignored. Restyling `icon` is the intended use. |

### The ctx struct

- `entry` - the `AnimalEntry` under construction. `entry.root` is the row's scroller element; its name label is `entry.root.text_label`.
- `animal` - the animal struct the row shows: a barn animal, the mount, the global `PET` for the pet row, or a throwaway `BaseAnimal` for adoption rows (built for the kind's `default_icon_variant` in the kind list, or for the offered variant in the variant list; its `idx` is random on every build, so never key state on it).
- `icon` - the `SpriteNode` showing the animal's icon. Handlers may call `icon.set_sprite(...)` and `icon.set_lut(lut, lut_index)` to show a per-animal appearance.

> [!NOTE]
> The emit lands where the constructor finishes the icon, before the row's helper methods are declared. Callers chain those helpers on after construction (`.add_sex_icon()`, `.add_heart_details()`, `.add_petting_details()` and so on), so the sex icon, hearts and checkboxes do not exist yet when this hook fires. Do not call the helpers from the handler. The adoption menu also blacks out a locked kind's or variant's icon (`set_color(c_black)`) after construction, so a restyled icon there still shows as a silhouette.

## Usage

```gml
// ui.animal_entry is an EVENT: the return value is ignored.
// Fires once per row each time an animal list is built.
function fresh_coat_animal_entry(_ctx) {
    if (_ctx.animal == PET) return; // the pet row: leave it, or handle PET here
    var _look = fresh_coat_look_for(_ctx.animal[$ "idx"]); // your own per-animal state
    if (_look == undefined) return;

    var _icon = fresh_coat_icon_for_look(_look); // { sprite, lut, lut_index }
    _ctx.icon.set_sprite(_icon.sprite);
    if (_icon.lut != undefined) {
        _ctx.icon.set_lut(_icon.lut, _icon.lut_index);
    }
}

// inside your latched register function (see Mod Anatomy):
mmapi_on("ui.animal_entry", fresh_coat_animal_entry);
```

Rows are a build-time snapshot. Nothing re-runs this hook while a menu stays open, so give the icon a think callback if it must track live state.

## Interactions

- The row's icon comes from [animal.icon](animal.icon.md) (for barn animals) or the pet prototype (for the pet row) before this hook fires, so a handler sees the already-filtered icon on `ctx.icon`.
- Only lists built from `AnimalEntry` fire. The mount picker popup, the adoption menu's header and the farm building selection menu's occupant list draw their icons straight from [animal.icon](animal.icon.md) without a row, so this hook never sees them.

## Engine Wiring

- Seam [`ui_animal_entry`](../seams/ui_animal_entry.md) dispatches from `gml/scripts/AnimalMenu.gml`, in the `AnimalEntry` constructor, right after the icon's sprite and LUT are set.

## See Also

- [animal.icon](animal.icon.md) - Swap the icon a barn animal kind or variant shows, wherever it is looked up.
- [animal.sprites](animal.sprites.md) - Swap the world sprites a barn or coop animal renders with.
- [pet.sprites](pet.sprites.md) - Swap the sprites the farm pet renders with.
- [ui.relationship_row_built](ui.relationship_row_built.md) - The same per-row moment for the NPC relationships journal.
