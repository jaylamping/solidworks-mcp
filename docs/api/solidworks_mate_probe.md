# solidworks_mate_probe

Create probe.

| Field | Value |
|-------|-------|
| Worker command | `mate_probe` |
| Tier | extended |
| Read only | true |
| Destructive | false |
| Confirm required | false |
| Description source | derived |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `use_selection` | boolean | no | — | Use the current SolidWorks selection instead of named references. |
| `selection_index` | integer | no | — | 1-based selection index when selecting a specific highlighted entity. |
| `path` | string | no | — | Optional document path under an allowed CAD root. (allowed root) |

## Tags

- mate

## Domains

- assembly
- mate

