# solidworks_open

Open a SolidWorks or STEP document from an allowed CAD root.

| Field | Value |
|-------|-------|
| Worker command | `open` |
| Tier | core |
| Read only | false |
| Destructive | false |
| Confirm required | false |
| Description source | authored |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | yes | - | SolidWorks or STEP document path under an allowed CAD root. (allowed root) |
| `start_if_missing` | boolean | no | - | Start SolidWorks if no instance is running. |

## Tags

- open

## Domains

- document

