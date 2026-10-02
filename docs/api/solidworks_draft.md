# solidworks_draft

Draft (taper) faces relative to a neutral plane/face, for molded or easier-to-release parts.

| Field | Value |
|-------|-------|
| Worker command | `draft` |
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
| `neutral` | unknown | yes | - | Neutral plane / face (pull direction). |
| `faces` | unknown | yes | - | Faces to draft. |
| `angle_deg` | number | yes | - | Value for angle deg. |
| `flip` | boolean | no | - | Value for flip. |
| `propagate` | `tangent`, `none`, `all_neutral`, `inner_loops`, `outer_loop` | no | - | Face propagation (default tangent, so filleted walls draft together). |
| `name` | string | no | - | Rename the created feature. |
| `rollback_on_error` | boolean | no | - | Delete the feature again if it rebuilds with an error (default true), so the part stays clean. |

## Tags

- feature
- modeling
- draft

## Domains

- document

