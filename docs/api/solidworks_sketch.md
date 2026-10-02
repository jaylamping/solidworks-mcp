# solidworks_sketch

Create (or extend) a sketch in one call: pick a plane or planar face with `on`, then add a batch of entities (line, centerline, circle, arc, arc3, rectangle, center_rectangle with corner_radius, polyline with corner_radius, slot, polygon, spline, point, ellipse). Inference is off so geometry lands exactly at the given coordinates. Returns the sketch name, closed-region count, and the sketch frame in model space. Start every boss/cut/revolve here.

| Field | Value |
|-------|-------|
| Worker command | `sketch` |
| Tier | core |
| Read only | false |
| Destructive | false |
| Confirm required | false |
| Description source | authored |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | yes | - | Part document to edit (.SLDPRT). Required so edits never land in whichever document happens to be active. (allowed root) |
| `units` | `mm`, `cm`, `m`, `in` | no | - | Length units for every length and coordinate in this call. Default mm. |
| `on` | unknown | no | - | Where to sketch: a plane name ("Front Plane", "Top Plane", "Right Plane", or a reference plane) or a planar face selector {face:{near:[x,y,z]}}. |
| `edit_sketch` | string | no | - | Reopen this existing sketch and add the entities to it instead of creating a new one. |
| `name` | string | no | - | Name for the new sketch. |
| `space` | `sketch`, `model` | no | - | Coordinate space of entity points. sketch (default): 2D [x,y] in the sketch plane's own axes (see sketchFrameInModel in the result). model: 3D [x,y,z] model points, projected onto the sketch plane. |
| `entities` | array | no | - | Value for entities. |
| `remove` | object[] | no | - | With edit_sketch: delete the line/arc/circle closest to each point (same coordinate space as entities; default max_distance 1) before adding entities. Combine with entities to move/resize geometry in undimensioned sketches. |
| `atomic` | boolean | no | - | If any entity fails, delete the new sketch and report (default true). |
| `fully_define` | boolean | no | - | Add relations and baseline dimensions from the origin so the sketch is fully defined and parametric; the result lists the new dimension names (usable with solidworks_set_dimensions / solidworks_equations). |

## Tags

- sketch
- modeling

## Domains

- document

