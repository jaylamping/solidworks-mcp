# solidworks_mate_coincident

Add a coincident mate between two component references or selected faces.

| Field | Value |
|-------|-------|
| Worker command | `mate_coincident` |
| Tier | core |
| Read only | false |
| Destructive | false |
| Confirm required | false |
| Description source | authored |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | no | - | Optional assembly path under an allowed CAD root. (allowed root) |
| `component_1` | string | no | - | First component name. |
| `ref_1` | string | no | - | Reference name or feature face on the first component. |
| `component_2` | string | no | - | Second component name. |
| `ref_2` | string | no | - | Reference name or feature face on the second component. |
| `face_index_1` | integer | no | - | Optional face index for the first reference. |
| `face_index_2` | integer | no | - | Optional face index for the second reference. |
| `align` | `aligned`, `anti_aligned` | no | - | Reference alignment direction. |
| `use_selection` | boolean | no | - | Use the current SolidWorks selection instead of named references. |
| `selection_index` | integer | no | - | 1-based selection index when selecting a specific highlighted entity. |

## Tags

- mate

## Domains

- assembly
- mate

