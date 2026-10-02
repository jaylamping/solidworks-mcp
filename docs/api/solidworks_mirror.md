# solidworks_mirror

Mirror features or bodies about a plane or planar face.

| Field | Value |
|-------|-------|
| Worker command | `mirror` |
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
| `plane` | unknown | yes | - | Mirror plane: plane name or planar face selector. |
| `features` | string[] | no | - | Value for features. |
| `bodies` | array | no | - | Mirror whole bodies instead of features. |
| `merge` | boolean | no | - | Bodies mode: merge the mirrored body with the original (default true). |
| `name` | string | no | - | Rename the created feature. |
| `rollback_on_error` | boolean | no | - | Delete the feature again if it rebuilds with an error (default true), so the part stays clean. |

## Tags

- feature
- modeling
- mirror

## Domains

- document

