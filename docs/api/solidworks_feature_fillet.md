# solidworks_feature_fillet

Manage fillet.

| Field | Value |
|-------|-------|
| Worker command | `feature_fillet` |
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
| `radius_m` | number | no | - | Value for radius m. |
| `radius_mm` | number | no | - | Value for radius mm. |
| `confirm` | const `true` | yes | `true` | Value for confirm. |

## Tags

- feature

## Domains

- document

