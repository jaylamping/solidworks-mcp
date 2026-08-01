# solidworks_set_component_transform

Apply a 4x4 transform matrix to an assembly component. Does not fix the component unless fix:true is passed.

| Field | Value |
|-------|-------|
| Worker command | `set_component_transform` |
| Tier | extended |
| Read only | false |
| Destructive | false |
| Confirm required | false |
| Description source | authored |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | yes | - | Value for path. (allowed root) |
| `component_name` | string | yes | - | Value for component name. |
| `matrix` | number[] | yes | - | Value for matrix. |
| `fix` | boolean | no | - | If true, fix the component after applying the transform. Defaults to false so the component stays floatable for mates/dragging. |
| `use_selection` | boolean | no | - | Use the current SolidWorks selection instead of named references. |
| `selection_index` | integer | no | - | 1-based selection index when selecting a specific highlighted entity. |

## Tags

- set

## Domains

- document

