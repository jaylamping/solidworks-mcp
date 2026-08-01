# solidworks_diagnose_com

Diagnose SolidWorks COM attach, ROT, and worker mutex health.

| Field | Value |
|-------|-------|
| Worker command | `diagnose_com` |
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
| `path` | string | no | - | Value for path. (allowed root) |
| `start_if_missing` | boolean | no | - | Value for start if missing. |

## Tags

- diagnose

## Domains

- document

