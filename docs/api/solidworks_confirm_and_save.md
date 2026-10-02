# solidworks_confirm_and_save

Worker command: confirm_and_save

| Field | Value |
|-------|-------|
| Worker command | `confirm_and_save` |
| Tier | core |
| Read only | false |
| Destructive | true |
| Confirm required | true |
| Description source | derived |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | yes | - | Value for path. (allowed root) |
| `looks_good` | const `true` | yes | `true` | Value for looks good. |
| `confirm` | const `true` | yes | `true` | Value for confirm. |
| `preview_path` | string | no | - | Value for preview path. |
| `reopen` | boolean | no | - | Value for reopen. |
| `pose_tolerance` | number | no | - | Value for pose tolerance. |

## Tags

- confirm

## Domains

- document

