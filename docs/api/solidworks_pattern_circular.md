# solidworks_pattern_circular

Circular pattern of features or bodies about an axis, cylindrical face, or circular edge (bolt circles, spokes, ribs).

| Field | Value |
|-------|-------|
| Worker command | `pattern_circular` |
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
| `features` | string[] | no | - | Value for features. |
| `bodies` | array | no | - | Value for bodies. |
| `axis` | unknown | yes | - | Axis name, cylindrical/conical face (its axis), circular edge (its axis), or linear edge. |
| `count` | integer | yes | - | Total instances including the seed. |
| `angle_deg` | number | no | - | Total angle covered with equal spacing (default 360). |
| `reverse` | boolean | no | - | Value for reverse. |
| `name` | string | no | - | Rename the created feature. |
| `rollback_on_error` | boolean | no | - | Delete the feature again if it rebuilds with an error (default true), so the part stays clean. |

## Tags

- feature
- modeling
- pattern

## Domains

- document

