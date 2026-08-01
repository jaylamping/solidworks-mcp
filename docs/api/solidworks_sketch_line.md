# solidworks_sketch_line

Create line.

| Field | Value |
|-------|-------|
| Worker command | `sketch_line` |
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
| `x1_m` | number | no | - | Value for x1 m. |
| `y1_m` | number | no | - | Value for y1 m. |
| `x2_m` | number | no | - | Value for x2 m. |
| `y2_m` | number | no | - | Value for y2 m. |

## Tags

- sketch

## Domains

- document

