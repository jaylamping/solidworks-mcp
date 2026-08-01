# solidworks_sketch_circle

Create circle.

| Field | Value |
|-------|-------|
| Worker command | `sketch_circle` |
| Tier | extended |
| Read only | false |
| Destructive | false |
| Confirm required | false |
| Description source | derived |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `use_selection` | boolean | no | - | Use the current SolidWorks selection instead of named references. |
| `selection_index` | integer | no | - | 1-based selection index when selecting a specific highlighted entity. |
| `path` | string | no | - | Optional document path under an allowed CAD root. (allowed root) |
| `center_x_m` | number | no | - | Value for center x m. |
| `center_y_m` | number | no | - | Value for center y m. |
| `radius_m` | number | no | - | Value for radius m. |

## Tags

- sketch

## Domains

- document

