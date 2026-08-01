# solidworks_transform_component

Transform component.

| Field | Value |
|-------|-------|
| Worker command | `transform_component` |
| Tier | extended |
| Read only | false |
| Destructive | false |
| Confirm required | false |
| Description source | derived |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | yes | - | Value for path. (allowed root) |
| `component_name` | string | yes | - | Value for component name. |
| `tx` | number | no | - | Value for tx. |
| `ty` | number | no | - | Value for ty. |
| `tz` | number | no | - | Value for tz. |
| `use_selection` | boolean | no | - | Use the current SolidWorks selection instead of named references. |
| `selection_index` | integer | no | - | 1-based selection index when selecting a specific highlighted entity. |

## Tags

- transform

## Domains

- document

