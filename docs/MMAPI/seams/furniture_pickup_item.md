# Seam: furniture_pickup_item

Filters the item a picked-up furniture node drops, right before it lands.

`furniture_pickup_item` is a **template seam** (`op = "filter"`). It feeds [furniture.pickup_item](../hooks/furniture.pickup_item.md). Mod authors never write seams. You register handlers for the hooks they dispatch. See [Seams](../SEAMS.md).

## Placement

| | |
| --- | --- |
| **File** | `gml/scripts/GameplaySystems/Data/Grid/GridActions/Pick.gml` |
| **Locator** | pristine context in `pick_node()`'s furniture branch, between the `is_date_photo` copy onto `live_item` and the `drop_item_to_ground(...)` call that drops it |
| **Op** | `filter` |
| **Feeds** | [`furniture.pickup_item`](../hooks/furniture.pickup_item.md) |
| **Var** | `live_item` |
| **ctx built** | `{ node: node, grid: data[2], source: "pick" }` |
| **Marker** | `mmapi_furniture_run_pickup_item` |

## The Edit

A pick on furniture (other than a rug) collects the node and, recursively, everything placed on it, sorts them deepest first and removes only the deepest layer, so a pick on a laden table lifts the items off it before the table itself. For each removed node the engine finds the item that places it (`find_item_prototype`, with house stairs falling back to their paired object), builds `live_item`, copies the node's infusion and, for a date photo, its `date_photo` onto it, and drops it at the original node's position.

The generated dispatch threads `live_item` through `mmapi_apply_filters("furniture.pickup_item", live_item, { node: node, grid: data[2], source: "pick" })` in the uniform try/catch shape, after both copies and before `drop_item_to_ground(...).setup(List(live_item))`. `node` is the node being removed, still intact (it is erased at the end of the loop body), and `data[2]` is the grid it sits in: the child grid for table-top pieces, the room's grid otherwise. A node with no placing item never reaches the dispatch, and a throwing handler keeps the engine's item (fail-open).

With zero handlers the filter dispatch early-outs on an empty registry, leaving pristine behavior.

## See Also

- [furniture.pickup_item](../hooks/furniture.pickup_item.md) - This is the hook this seam dispatches.
- [furniture_pickup_item_rug](furniture_pickup_item_rug.md) - The rug branch of the same pick.
- [furniture_pickup_item_poof](furniture_pickup_item_poof.md) - The house-stairs poof site for the same hook.
- [pick_node_modifier](pick_node_modifier.md) - The modifier filter at the head of the same `pick_node()`.
