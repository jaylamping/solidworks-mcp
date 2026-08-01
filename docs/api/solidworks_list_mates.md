# solidworks_list_mates

Return mate feature names and types for an assembly.

| Field | Value |
|-------|-------|
| Worker command | `list_mates` |
| Tier | core |
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

## Tags

- list

## Domains

- assembly
- mate

