# Seam: furniture_pickup_item_rug

Rebuilds the rug pick's item drop inline so the dropped item can be filtered.

`furniture_pickup_item_rug` is a **text seam** (`anchor` + `replace`). It feeds [furniture.pickup_item](../hooks/furniture.pickup_item.md). Mod authors never write seams. You register handlers for the hooks they dispatch. See [Seams](../SEAMS.md).

## Placement

| | |
| --- | --- |
| **File** | `gml/scripts/GameplaySystems/Data/Grid/GridActions/Pick.gml` |
| **Locator** | text anchor on `pick_node()`'s `if is_rug_pick {` branch: its `drop_item_from_object(...)` call and the `erase_rug_node(grid, inst_index)` that follows |
| **Feeds** | [`furniture.pickup_item`](../hooks/furniture.pickup_item.md) |
| **Value filtered** | the rug's `LiveItem`, built inline |
| **ctx built** | `{ node: node, grid: grid, source: "rug" }` |
| **Marker** | `mmapi_furniture_run_pickup_item_rug` |

## The Edit

The pristine rug branch drops its item through the shared helper `drop_item_from_object(object_id, x, y, infusion)`, which builds the `LiveItem` and hands it to a new `obj_item` in one call, leaving no point to filter the item without changing every other caller of the helper. The text seam replaces that one call with the helper's own body, inlined at the rug site: it looks up the placing item with `find_item_prototype(node.object_id)`, creates the `obj_item` at the rug's renderer position with `move_z = true`, builds the `LiveItem`, and copies the rug's infusion onto it when the item has none, exactly as the helper does.

Between building the item and `setup(List(item))` it calls `mmapi_apply_filters("furniture.pickup_item", <item>, { node: node, grid: grid, source: "rug" })` under its own try/catch, so a throwing handler keeps the engine's item. `node` is the rug node (from `grid.node_rug_parent`), still intact until the `erase_rug_node(grid, inst_index)` that ends the replacement, unchanged from pristine. A rug with no placing item drops nothing, as before. The helper itself is untouched, so its other callers behave exactly as in pristine.

With zero handlers the inlined drop is behaviorally equivalent to the helper call it replaces, and the filter early-outs on an empty registry.

## See Also

- [furniture.pickup_item](../hooks/furniture.pickup_item.md) - This is the hook this seam dispatches.
- [furniture_pickup_item](furniture_pickup_item.md) - The non-rug furniture branch of the same pick.
- [furniture_pickup_item_poof](furniture_pickup_item_poof.md) - The house-stairs poof site for the same hook.
