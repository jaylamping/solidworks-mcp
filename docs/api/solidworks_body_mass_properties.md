# solidworks_body_mass_properties

Per-body mass properties of a part, or of an assembly component's part: visibility, volume, surface area, mass, centre of mass and inertia for every solid body, plus totals for all, visible and hidden bodies. Hidden bodies (for example embedded reference copies of purchased parts) still count in SOLIDWORKS mass properties; totals.visible leaves them out.

| Field | Value |
|-------|-------|
| Worker command | `body_mass_properties` |
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
| `component_name` | string | no | - | Assembly component whose part to report (path is the assembly); centres of mass are also given in assembly coordinates. |
| `bodies` | string[] | no | - | Only report these solid bodies, by name. |
| `visible_only` | boolean | no | - | Skip hidden bodies. By default they are listed and flagged, because SOLIDWORKS mass properties include them. |

## Tags

- get
- mass

## Domains

- document

