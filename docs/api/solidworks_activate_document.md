# solidworks_activate_document

Activate document.

| Field | Value |
|-------|-------|
| Worker command | `activate_document` |
| Tier | extended |
| Read only | false |
| Destructive | false |
| Confirm required | false |
| Description source | derived |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | yes | - | SolidWorks or STEP document path under an allowed CAD root. (allowed root) |
| `start_if_missing` | boolean | no | - | Start SolidWorks if no instance is running. |

## Tags

- activate

## Domains

- document

