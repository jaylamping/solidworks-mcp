# solidworks_component_mass_properties

Return mass, center of mass, and inertia for a component.

| Field | Value |
|-------|-------|
| Worker command | `component_mass_properties` |
| Tier | extended |
| Read only | true |
| Destructive | false |
| Confirm required | false |
| Description source | authored |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `use_selection` | boolean | no | - | Use the current SolidWorks selection instead of named references. |
| `selection_index` | integer | no | - | 1-based selection index when selecting a specific highlighted entity. |
| `path` | string | no | - | Optional document path under an allowed CAD root. (allowed root) |
| `component_name` | string | no | - | Value for component name. |

## Tags

- component

## Domains

- document

