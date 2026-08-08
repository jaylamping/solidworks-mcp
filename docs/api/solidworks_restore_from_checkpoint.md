# solidworks_restore_from_checkpoint

Worker command: restore_from_checkpoint

Restore an assembly/part from a `.checkpoints` snapshot. Stages a `pre_restore` checkpoint of the current target first.

| Field | Value |
|-------|-------|
| Worker command | `restore_from_checkpoint` |
| Tier | extended |
| Read only | false |
| Destructive | true |
| Confirm required | true |

## Args

| Name | Type | Required | Notes |
|------|------|----------|-------|
| `checkpoint_path` | string | yes | Path under `.checkpoints/` |
| `path` | string | no | Target document; inferred from checkpoint filename when omitted |
| `close_open_document` | boolean | no | Close the open doc before overwrite (default true) |
| `confirm` | `true` | yes | Required for this destructive restore |

## Tags

- checkpoint
- restore

## Domains

- document
