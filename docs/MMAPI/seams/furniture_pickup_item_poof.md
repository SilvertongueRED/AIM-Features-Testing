# Seam: furniture_pickup_item_poof

Filters the item a furniture node turns into when house stairs poof it into lost items.

`furniture_pickup_item_poof` is a **template seam** (`op = "filter"`). It feeds [furniture.pickup_item](../hooks/furniture.pickup_item.md). Mod authors never write seams. You register handlers for the hooks they dispatch. See [Seams](../SEAMS.md).

## Placement

| | |
| --- | --- |
| **File** | `gml/scripts/GameplaySystems/Data/Grid/__GridGeneral/GridUtils.gml` |
| **Locator** | pristine context in `poof_furniture_to_items()`, between the infusion copy onto `item` and the `lost_items_list.push({` that records it |
| **Op** | `filter` |
| **Feeds** | [`furniture.pickup_item`](../hooks/furniture.pickup_item.md) |
| **Var** | `item` |
| **ctx built** | `{ node: grid.node_parent[ni], grid: grid, source: "poof" }` |
| **Marker** | `mmapi_furniture_run_pickup_item_poof` |

## The Edit

Placing house stairs writes the paired staircase into the corresponding room on the other floor, and `poof_furniture_to_items()` first clears that footprint: every furniture node in it (and, recursively, everything placed on those nodes) becomes an entry in that room's lost-items list and is erased. For each node with a placing item it builds `item`, copies the node's infusion onto it, and pushes `{ x, y, items: List(item) }`.

The generated dispatch threads `item` through `mmapi_apply_filters("furniture.pickup_item", item, { node: grid.node_parent[ni], grid: grid, source: "poof" })` in the uniform try/catch shape, after the infusion copy and before the push. `grid.node_parent[ni]` is the node being poofed, still intact (it is erased a few lines later), and `grid` is the grid being cleared, a child grid on the recursive calls. The engine copies no date photo at this site, so the incoming item never carries one.

With zero handlers the filter dispatch early-outs on an empty registry, leaving pristine behavior.

## See Also

- [furniture.pickup_item](../hooks/furniture.pickup_item.md) - This is the hook this seam dispatches.
- [furniture_pickup_item](furniture_pickup_item.md) - The pick-tool site for the same hook.
- [furniture_pickup_item_rug](furniture_pickup_item_rug.md) - The rug pick site for the same hook.
