# solidworks_round_side_arms_from_circle

Round side arms from circle.

| Field | Value |
|-------|-------|
| Worker command | `round_side_arms_from_circle` |
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
| `path` | string | no | - | Value for path. (allowed root) |
| `plane_name` | string | no | - | Value for plane name. |
| `center_x_m` | number | no | - | Value for center x m. |
| `center_y_m` | number | no | - | Value for center y m. |
| `radius_m` | number | no | - | Value for radius m. |
| `samples` | integer | no | - | Value for samples. |
| `dry_run` | boolean | no | - | Value for dry run. |
| `save` | boolean | no | - | Value for save. |
| `confirm` | const `true` | yes | `true` | Value for confirm. |

## Tags

- round

## Domains

- document

