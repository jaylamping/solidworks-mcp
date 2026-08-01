# solidworks_delete_mate

Delete a named mate feature from an assembly.

| Field | Value |
|-------|-------|
| Worker command | `delete_mate` |
| Tier | extended |
| Read only | false |
| Destructive | true |
| Confirm required | true |
| Description source | authored |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | yes | - | Value for path. (allowed root) |
| `mate_name` | string | yes | - | Value for mate name. |
| `confirm` | const `true` | yes | `true` | Value for confirm. |

## Tags

- delete

## Domains

- assembly
- mate

