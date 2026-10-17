# Hook: object.renderer_build

Know when any grid object's world renderer is built, and give one placed object its own prototype for the session.

`object.renderer_build` is an **event** hook. Register a callback with `mmapi_on`. See [Hooks](../HOOKS.md) for how registration and dispatch work.

## Contract

Fires at the head of `Grid.initialize_node_renderer(node)`, the single dispatcher every grid object's world renderer is built through (furniture, rugs, table-top furniture in child grids, buildings, trees, crops, rocks, bushes, stumps, breakables, dig sites and grass), before the category-specific `create_*_renderer` runs and before it reads `node.prototype`. ctx is `{ node, grid }`. The return value is ignored.

It fires on placement, on every room entry and grid load, after a save loads, and whenever a child grid is rebuilt. It never fires per frame.

| | |
| --- | --- |
| **Fires** | At the head of `Grid.initialize_node_renderer(node)`, before the category's `create_*_renderer` reads `node.prototype`. |
| **ctx** | `{ node, grid }` |
| **Kind contract** | The callback observes the moment. Its return value is ignored. The one permitted mutation is replacing `node.prototype` with a shallow clone (below). |

### The ctx struct

- `node` - the grid node whose renderer is about to be built. Read `node.object_id`, `node.prototype`, and `node.top_left_x` / `node.top_left_y`. Furniture nodes also carry `node.cardinal_index` (their rotation).
- `grid` - the `Grid` building it. For furniture placed on furniture this is the child grid, not the room's grid.

### The one permitted mutation

This is an observation hook whose contract allows exactly one change: a handler may replace `node.prototype` with a **shallow clone of the same object's prototype** (same `object_id` and category, sub-structs shared or shallow-copied, sprite fields changed). The build, and every later live read of `node.prototype`, then use the clone.

`node.prototype` is never saved (Grid serialization skips it), and the engine re-points it at `NODE_PROTOTYPES` on every load. A clone therefore lives only for the session, and the next build after a load fires this hook again so the handler can re-apply it. Never change `node.object_id`: that is what the save stores, and it must keep naming the object the node really is.

> [!IMPORTANT]
> Clone, never edit. `node.prototype` is the shared `NODE_PROTOTYPES` entry, so writing a sprite field on it in place changes **every** placed copy of that object, the placement ghost, and every later placement. Copy the struct (and any sub-struct you change, such as one `cardinal_data` entry) before writing to it.

> [!NOTE]
> A changed decision applies on the node's next build (room entry, grid reload, or picking the piece up and placing it again), not instantly on a live world. To restyle a renderer that is already on screen, also change it directly, or use the per-sprite hooks below.

## Usage

```gml
// A one-level copy: the new struct shares every child with the original.
function fresh_coat_shallow_copy(_struct) {
    var _copy = {};
    var _names = struct_get_names(_struct);
    for (var i = 0; i < array_length(_names); i++) {
        _copy[$ _names[i]] = _struct[$ _names[i]];
    }
    return _copy;
}

// object.renderer_build is an EVENT: the return value is ignored.
// It fires once per renderer build, never per frame.
function fresh_coat_renderer_build(_ctx) {
    var _node = _ctx.node;
    if (_node.object_id != fresh_coat_target_object()) return; // cheap test first
    if (!fresh_coat_wants_alt_look(_node)) return; // your own per-object choice

    // Copy the shared prototype, then copy the one sub-struct you change
    // (a fresh cardinal_data array with a copied entry), so no other placed
    // copy of the object, and no placement ghost, is affected.
    var _proto = fresh_coat_shallow_copy(_node.prototype);
    var _cardinals = array_create(array_length(_proto.cardinal_data), undefined);
    for (var i = 0; i < array_length(_cardinals); i++) {
        var _cd = _proto.cardinal_data[i];
        if (is_struct(_cd)) {
            _cd = fresh_coat_shallow_copy(_cd);
            _cd.sprite = fresh_coat_alt_sprite(_cd.sprite);
        }
        _cardinals[i] = _cd;
    }
    _proto.cardinal_data = _cardinals;
    _node.prototype = _proto; // node.object_id stays as it is
}

// inside your latched register function (see Mod Anatomy):
mmapi_on("object.renderer_build", fresh_coat_renderer_build);
```

`fresh_coat_target_object`, `fresh_coat_wants_alt_look` and `fresh_coat_alt_sprite` stand for your own lookups. A handler runs for every grid object in a room on each load, so make its first check cheap.

## Interactions

- The clone is what [furniture.floor_sprite](furniture.floor_sprite.md) and [object.node_sprite](object.node_sprite.md) see afterwards, since both read the node's live prototype. The three compose: this hook decides which prototype a node builds from, the sprite filters can still adjust single sprites.
- Picking a furniture piece up drops the item [furniture.pickup_item](furniture.pickup_item.md) decides. Pair the two when a per-object look should survive being picked up and placed again.

## Engine Wiring

- Seam [`object_renderer_build`](../seams/object_renderer_build.md) dispatches from `gml/scripts/GameplaySystems/Data/Grid/__GridGeneral/Grid.gml`, at the head of `initialize_node_renderer(node)`.

## See Also

- [furniture.floor_sprite](furniture.floor_sprite.md) - Swap a furniture piece's floor sprite as its renderer is built.
- [object.node_sprite](object.node_sprite.md) - Swap the sprite of any world node before it draws.
- [furniture.pickup_item](furniture.pickup_item.md) - Change the item a furniture piece turns back into.
- [furniture.preview_sprite](furniture.preview_sprite.md) - The held-placement ghost, which has no node and never fires this hook.
