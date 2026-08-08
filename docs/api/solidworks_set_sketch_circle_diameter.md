# solidworks_set_sketch_circle_diameter

Set a sketch circle diameter or adjust it by a delta.

| Field | Value |
|-------|-------|
| Worker command | `set_sketch_circle_diameter` |
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
| `sketch_name` | string | yes | - | Value for sketch name. |
| `diameter_m` | number | no | - | Value for diameter m. |
| `diameter_mm` | number | no | - | Value for diameter mm. |
| `delta_m` | number | no | - | Value for delta m. |
| `delta_mm` | number | no | - | Value for delta mm. |
| `match_diameter_m` | number | no | - | Value for match diameter m. |
| `match_diameter_mm` | number | no | - | Value for match diameter mm. |
| `prefer_inner` | boolean | no | - | Value for prefer inner. |
| `confirm` | const `true` | yes | `true` | Value for confirm. |

## Tags

- set
- sketch

## Domains

- document

