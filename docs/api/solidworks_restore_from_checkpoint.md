# solidworks_restore_from_checkpoint

Worker command: restore_from_checkpoint

| Field | Value |
|-------|-------|
| Worker command | `restore_from_checkpoint` |
| Tier | extended |
| Read only | false |
| Destructive | true |
| Confirm required | true |
| Description source | derived |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `checkpoint_path` | string | yes | - | Value for checkpoint path. |
| `path` | string | no | - | Value for path. (allowed root) |
| `close_open_document` | boolean | no | - | Value for close open document. |
| `confirm` | const `true` | yes | `true` | Value for confirm. |

## Tags

- restore

## Domains

- document

