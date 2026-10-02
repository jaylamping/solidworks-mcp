# solidworks_revolve

Revolve a sketch profile about its centerline (or an axis/edge) as a boss or cut. Use for shafts, pulleys, bushings, bearing seats, grooves.

| Field | Value |
|-------|-------|
| Worker command | `revolve` |
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
| `sketch` | string | yes | - | Profile sketch. Must lie entirely on one side of the axis. |
| `mode` | `boss`, `cut` | no | - | Value for mode. |
| `axis` | unknown | no | - | Axis: omit to use the sketch's single centerline; or an axis name, a linear edge, or an edge/line selector. |
| `angle_deg` | number | no | - | Default 360. |
| `mid_plane` | boolean | no | - | Value for mid plane. |
| `reverse` | boolean | no | - | Value for reverse. |
| `angle2_deg` | number | no | - | Second-direction angle. |
| `thin` | object | no | - | Value for thin. |
| `merge` | boolean | no | - | Value for merge. |
| `name` | string | no | - | Rename the created feature. |
| `rollback_on_error` | boolean | no | - | Delete the feature again if it rebuilds with an error (default true), so the part stays clean. |

## Tags

- feature
- modeling
- revolve

## Domains

- document

