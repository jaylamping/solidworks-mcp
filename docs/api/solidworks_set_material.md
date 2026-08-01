# solidworks_set_material

Set material.

| Field | Value |
|-------|-------|
| Worker command | `set_material` |
| Tier | extended |
| Read only | false |
| Destructive | true |
| Confirm required | true |
| Description source | derived |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `use_selection` | boolean | no | — | Use the current SolidWorks selection instead of named references. |
| `selection_index` | integer | no | — | 1-based selection index when selecting a specific highlighted entity. |
| `path` | string | no | — | Optional document path under an allowed CAD root. (allowed root) |
| `material` | string | yes | — | Value for material. |
| `database` | string | no | — | Value for database. |
| `confirm` | const `true` | yes | `true` | Value for confirm. |

## Tags

- set

## Domains

- assembly
- mate

