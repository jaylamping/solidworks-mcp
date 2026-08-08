# solidworks_list_checkpoints

Worker command: list_checkpoints

List recent staged rollback snapshots for a document.

| Field | Value |
|-------|-------|
| Worker command | `list_checkpoints` |
| Tier | core |
| Read only | true |
| Destructive | false |
| Confirm required | false |

## Args

| Name | Type | Required | Notes |
|------|------|----------|-------|
| `path` | string | yes | Working assembly/part path |

## Tags

- checkpoint

## Domains

- document
