# solidworks_confirm_and_save

After the user says the viewport looks good: checkpoint, lock pose, heal warning mates, save, and verify the pose did not jump.

| Field | Value |
|-------|-------|
| Worker command | `confirm_and_save` |
| Tier | core |
| Read only | false |
| Destructive | true |
| Confirm required | true |
| Description source | authored |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | no | - | Assembly/part path under an allowed CAD root. Defaults to the active document. (allowed root) |
| `looks_good` | const `true` | yes | `true` | User visually approved the current viewport. Do not set this until the user answers yes to 'Does this look good?'. |
| `confirm` | const `true` | yes | `true` | Acknowledge the save/heal operation. |
| `preview_path` | string | no | - | Optional PNG path for a keep_view screenshot captured before save. |
| `checkpoint` | boolean | no | - | Create a checkpoint before save (default true). |
| `pose_tolerance` | number | no | - | Max sum of absolute transform deltas allowed after save/rebuild. |
| `max_heal_attempts` | integer | no | - | How many fix/save/restore cycles to try when the pose jumps. |

## Tags

- confirm

## Domains

- document

