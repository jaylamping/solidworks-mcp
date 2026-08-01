# solidworks_sketch_rectangle

Create rectangle.

| Field | Value |
|-------|-------|
| Worker command | `sketch_rectangle` |
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
| `x1_m` | number | no | - | Value for x1 m. |
| `y1_m` | number | no | - | Value for y1 m. |
| `x2_m` | number | no | - | Value for x2 m. |
| `y2_m` | number | no | - | Value for y2 m. |
| `confirm` | const `true` | yes | `true` | Value for confirm. |

## Tags

- sketch

## Domains

- document

