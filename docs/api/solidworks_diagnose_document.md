# solidworks_diagnose_document

Diagnose document.

| Field | Value |
|-------|-------|
| Worker command | `diagnose_document` |
| Tier | debug |
| Read only | true |
| Destructive | false |
| Confirm required | false |
| Description source | derived |

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

