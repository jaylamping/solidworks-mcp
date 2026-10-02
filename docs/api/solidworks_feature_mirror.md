# solidworks_feature_mirror

Legacy: mirror one feature about a named plane. Prefer solidworks_mirror (features or bodies, faces as planes).

| Field | Value |
|-------|-------|
| Worker command | `feature_mirror` |
| Tier | extended |
| Read only | false |
| Destructive | true |
| Confirm required | true |
| Description source | authored |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `use_selection` | boolean | no | - | Use the current SolidWorks selection instead of named references. |
| `selection_index` | integer | no | - | 1-based selection index when selecting a specific highlighted entity. |
| `path` | string | no | - | Optional document path under an allowed CAD root. (allowed root) |
| `feature_name` | string | yes | - | Value for feature name. |
| `plane_name` | string | no | - | Value for plane name. |
| `confirm` | const `true` | yes | `true` | Value for confirm. |

## Tags

- feature

## Domains

- document

