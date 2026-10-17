# Hook: furniture.pickup_item

Change the item a furniture piece turns back into when it is picked up.

`furniture.pickup_item` is a **filter** hook. Register a callback with `mmapi_filter`. See [Hooks](../HOOKS.md) for how registration and dispatch work.

## Contract

Fires when picking up a placed furniture object produces its item: for every furniture node removed by a pick (including rugs and items lifted off tables), and for every furniture node poofed into lost items when house stairs are placed over it. The filtered value is the `LiveItem` about to be dropped, already carrying the node's infusion and date photo. ctx is `{ node, grid, source }`. Return a replacement `LiveItem`, or `undefined` to keep the engine's item. Fires once per removed node, not per frame.

| | |
| --- | --- |
| **Fires** | Once per removed furniture node that yields an item: a pick, a rug pick, or a poof under new house stairs. |
| **Value** | The `LiveItem` about to be dropped, with the node's infusion (and, for date photos picked up normally, its date photo) already copied on. |
| **ctx** | `{ node, grid, source }` |
| **Kind contract** | The callback receives the current value and returns a replacement, or `undefined` to keep the current value. |

### The ctx struct

- `node` - the furniture node being removed. It is still readable, including any fields a mod stored on it, and it is erased right after the item is dropped.
- `grid` - the grid the node sits in: a child grid for an item lifted off a table, the room's grid otherwise.
- `source` - names the dispatch site:
  - `"pick"` - a furniture node removed by the pick tool, items lifted off tables included.
  - `"rug"` - a rug removed by the pick tool.
  - `"poof"` - a furniture node turned into a lost item because house stairs were placed over its cell on the other floor.

> [!IMPORTANT]
> Copy `infusion` and `date_photo` onto a replacement you build yourself, or the player loses them: the engine copied them onto the value it passes in, not onto your new item.

> [!NOTE]
> Only nodes that turn back into an item dispatch. The engine finds the item as the first item whose `object` places the node's object (house stairs fall back to their paired object), and a node with no such item drops nothing and never reaches this hook. A pick the engine refuses (an animal toy in use, a cradle with a child in it, a piece outside its placeable locations) also never fires.

## Usage

```gml
// furniture.pickup_item is a FILTER: you receive (value, ctx) and return a
// replacement, or undefined to keep the game's value.
function fresh_coat_pickup_item(_value, _ctx) {
    if (_value == undefined) return undefined; // test undefined BEFORE anything else
    // _ctx is { node, grid, source }; source is "pick", "rug" or "poof".
    var _item_id = fresh_coat_item_for_node(_ctx.node); // your own lookup, e.g. a field you stored on the node
    if (_item_id == undefined) return undefined;

    var _item = new LiveItem(_item_id);
    _item.infusion = _value.infusion;     // keep what the engine copied on
    _item.date_photo = _value.date_photo;
    return _item;
}

// inside your latched register function (see Mod Anatomy):
mmapi_filter("furniture.pickup_item", fresh_coat_pickup_item);
```

A typical replacement is a different item id whose `object` places a look-alike object, so the piece keeps its appearance when it is placed again.

## Interactions

- The item goes straight into a new world item (`drop_item_to_ground(...).setup(...)` for a pick, an `obj_item` for a rug) or onto the other floor's lost-items list (a poof), never through `drop_item()`. [items.dropped](items.dropped.md) therefore does not see furniture pick-ups; this hook is the place to observe them.
- Pair with [object.renderer_build](object.renderer_build.md) when a per-object appearance chosen on a placed node should travel with the item it turns back into.

## Engine Wiring

- Seam [`furniture_pickup_item`](../seams/furniture_pickup_item.md) dispatches from `gml/scripts/GameplaySystems/Data/Grid/GridActions/Pick.gml`, in `pick_node()`'s furniture branch, between the date-photo copy and `drop_item_to_ground` (`source: "pick"`).
- Seam [`furniture_pickup_item_rug`](../seams/furniture_pickup_item_rug.md) dispatches from the same file's rug branch, which it rewrites to build the dropped item inline so it can be filtered (`source: "rug"`).
- Seam [`furniture_pickup_item_poof`](../seams/furniture_pickup_item_poof.md) dispatches from `gml/scripts/GameplaySystems/Data/Grid/__GridGeneral/GridUtils.gml`, in `poof_furniture_to_items()`, before the item joins the lost-items list (`source: "poof"`).

## See Also

- [furniture.place_guard](furniture.place_guard.md) - Veto a furniture placement before it is written.
- [object.renderer_build](object.renderer_build.md) - Give one placed object its own prototype as its renderer is built.
- [items.give](items.give.md) - Rewrite any item the player is about to receive.
