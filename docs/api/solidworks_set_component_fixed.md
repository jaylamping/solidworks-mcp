# solidworks_set_component_fixed

Set component fixed.

| Field | Value |
|-------|-------|
| Worker command | `set_component_fixed` |
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
| `component_name` | string | no | - | Value for component name. |

## Tags

- set

## Domains

- document

