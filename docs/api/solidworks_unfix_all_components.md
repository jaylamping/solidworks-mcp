# solidworks_unfix_all_components

Unfix all components.

| Field | Value |
|-------|-------|
| Worker command | `unfix_all_components` |
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
| `confirm` | const `true` | yes | `true` | Acknowledge the requested state-changing operation. |

## Tags

- unfix

## Domains

- document

