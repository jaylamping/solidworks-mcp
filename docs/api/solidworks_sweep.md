# solidworks_sweep

Sweep a closed profile sketch along a path sketch/edges as a boss or cut (tubes, cable channels, handles).

| Field | Value |
|-------|-------|
| Worker command | `sweep` |
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
| `profile` | string | no | - | Closed profile sketch at the path start. |
| `circular_profile_diameter` | number | no | - | Sweep a round section of this diameter instead of a profile sketch (tubes, wires, O-ring grooves). |
| `sweep_path` | unknown | yes | - | Path: sketch or helix name, or edge selector(s). The profile should be pierced by / perpendicular to the path start. |
| `guides` | unknown | no | - | Guide curves (sketches/edges). |
| `path_alignment` | `auto`, `none`, `minimum_twist` | no | - | auto: minimum twist for helix paths (threads, springs), none otherwise. |
| `mode` | `boss`, `cut` | no | - | Value for mode. |
| `twist_deg` | number | no | - | Value for twist deg. |
| `merge` | boolean | no | - | Value for merge. |
| `name` | string | no | - | Rename the created feature. |
| `rollback_on_error` | boolean | no | - | Delete the feature again if it rebuilds with an error (default true), so the part stays clean. |

## Tags

- feature
- modeling
- sweep

## Domains

- document

