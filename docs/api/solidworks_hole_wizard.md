# solidworks_hole_wizard

Standards-based ISO holes via Hole Wizard: tapped (with cosmetic thread), socket-head counterbore, flat-head countersink, clearance, dowel; one or many positions on a face. Use for machined parts; solidworks_hole for printed parts.

| Field | Value |
|-------|-------|
| Worker command | `hole_wizard` |
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
| `type` | `tapped`, `counterbore`, `countersink`, `clearance` | no | - | tapped (default) = ISO tapped hole with cosmetic thread; counterbore = ISO socket head cap screw; countersink = ISO flat head; clearance = ISO 273 normal fit. For dowel/press-fit holes use solidworks_hole with an exact diameter. |
| `size` | string | no | - | ISO size, e.g. M3, M4, M5 (tapped also accepts M3x0.5). |
| `on` | unknown | yes | - | Planar face the holes start on. |
| `positions` | array[] | yes | - | Hole centers as model points on that face. |
| `depth` | number | no | - | Drill depth; omit for through-all. |
| `thread_depth` | number | no | - | Value for thread depth. |
| `cosmetic_thread` | boolean | no | - | Value for cosmetic thread. |
| `name` | string | no | - | Rename the created feature. |
| `rollback_on_error` | boolean | no | - | Delete the feature again if it rebuilds with an error (default true), so the part stays clean. |

## Tags

- feature
- modeling
- hole
- thread

## Domains

- document

