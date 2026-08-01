# solidworks_feature_circular_pattern

Manage circular pattern.

| Field | Value |
|-------|-------|
| Worker command | `feature_circular_pattern` |
| Tier | extended |
| Read only | false |
| Destructive | true |
| Confirm required | true |
| Description source | derived |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `use_selection` | boolean | no | - | Use the current SolidWorks selection instead of named references. |
| `selection_index` | integer | no | - | 1-based selection index when selecting a specific highlighted entity. |
| `path` | string | no | - | Optional document path under an allowed CAD root. (allowed root) |
| `feature_name` | string | yes | - | Value for feature name. |
| `count` | integer | no | - | Value for count. |
| `angle_deg` | number | no | - | Value for angle deg. |
| `axis_name` | string | no | - | Value for axis name. |
| `confirm` | const `true` | yes | `true` | Value for confirm. |

## Tags

- feature

## Domains

- document

