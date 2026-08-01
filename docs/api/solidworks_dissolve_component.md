# solidworks_dissolve_component

Dissolve component.

| Field | Value |
|-------|-------|
| Worker command | `dissolve_component` |
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
| `component_name` | string | yes | - | Value for component name. |
| `confirm` | const `true` | yes | `true` | Value for confirm. |

## Tags

- dissolve

## Domains

- document

