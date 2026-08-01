# solidworks_feature_linear_pattern

Manage linear pattern.

| Field | Value |
|-------|-------|
| Worker command | `feature_linear_pattern` |
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
| `spacing_m` | number | no | - | Value for spacing m. |
| `spacing_mm` | number | no | - | Value for spacing mm. |
| `direction` | `X`, `Y` | no | - | Value for direction. |
| `confirm` | const `true` | yes | `true` | Value for confirm. |

## Tags

- feature

## Domains

- document

