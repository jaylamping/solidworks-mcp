# solidworks_select_by_persist_reference

Select an entity using a base64 persist reference.

| Field | Value |
|-------|-------|
| Worker command | `select_by_persist_reference` |
| Tier | extended |
| Read only | false |
| Destructive | false |
| Confirm required | false |
| Description source | authored |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | yes | - | Value for path. (allowed root) |
| `persist_reference` | string | yes | - | Value for persist reference. |
| `mark` | integer | no | - | Value for mark. |
| `append` | boolean | no | - | Value for append. |

## Tags

- select

## Domains

- document

