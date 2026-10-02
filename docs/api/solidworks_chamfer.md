# solidworks_chamfer

Chamfer edges chosen by selector: angle-distance (default 45°) or distance-distance.

| Field | Value |
|-------|-------|
| Worker command | `chamfer` |
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
| `distance` | number | yes | - | Value for distance. |
| `angle_deg` | number | no | - | Angle-distance chamfer angle (default 45). |
| `distance2` | number | no | - | Use distance-distance chamfer with this second distance. |
| `edges` | unknown | yes | - | One selector or an array of selectors (see selector grammar on solidworks_sketch.on). |
| `name` | string | no | - | Rename the created feature. |
| `rollback_on_error` | boolean | no | - | Delete the feature again if it rebuilds with an error (default true), so the part stays clean. |

## Tags

- feature
- modeling
- chamfer

## Domains

- document

