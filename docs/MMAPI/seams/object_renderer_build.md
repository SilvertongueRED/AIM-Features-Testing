# Seam: object_renderer_build

Emits at the head of the one dispatcher every grid object's world renderer is built through.

`object_renderer_build` is a **template seam** (`op = "emit"`). It feeds [object.renderer_build](../hooks/object.renderer_build.md). Mod authors never write seams. You register handlers for the hooks they dispatch. See [Seams](../SEAMS.md).

## Placement

| | |
| --- | --- |
| **File** | `gml/scripts/GameplaySystems/Data/Grid/__GridGeneral/Grid.gml` |
| **Locator** | structural target: `initialize_node_renderer`, at head |
| **Op** | `emit` |
| **Feeds** | [`object.renderer_build`](../hooks/object.renderer_build.md) |
| **ctx built** | `{ node: node, grid: self }` |
| **Marker** | `mmapi_object_run_renderer_build` |

## The Edit

The generated emit lands as the first statement of `Grid.initialize_node_renderer(node)`, calling `mmapi_emit("object.renderer_build", { node: node, grid: self })` in the uniform try/catch shape. The function is a single `switch` over the node's object category that hands the node to the matching `create_*_renderer` (dig sites, trees, rocks, breakables, stumps, crops, bushes, grass, furniture and buildings), so one emit at its head covers every grid object, rugs and table-top furniture in child grids included, and it runs before any of those builders reads `node.prototype`.

That ordering is what lets a handler swap `node.prototype` for a session-only clone and have the whole build use it. `self` is the `Grid` doing the build, which for furniture placed on furniture is the child grid. The callers are the grid's own `write_node` (placement), its whole-grid renderer pass on room entry and grid load, and the grass and debug rebuild helpers, so the emit fires per build and never per frame.

With zero handlers the emit early-outs on an empty registry, leaving pristine behavior.

## See Also

- [object.renderer_build](../hooks/object.renderer_build.md) - This is the hook this seam dispatches.
- [furniture_floor_sprite](furniture_floor_sprite.md) - The furniture floor-sprite filter that runs inside the build this seam precedes.
- [node_renderer_set_sprite](node_renderer_set_sprite.md) - The per-sprite filter on world node renderers.
