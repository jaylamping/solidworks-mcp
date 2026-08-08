# solidworks_confirm_and_save

Hard lock-in gate after the user explicitly approves a pose/mate change.

| Field | Value |
|-------|-------|
| Worker command | `confirm_and_save` |
| Tier | core |
| Read only | false |
| Destructive | true |
| Confirm required | true (`confirm: true` and `looks_good: true`) |

## What it does

1. Stages a forced pre-save checkpoint
2. Force-rebuilds the document
3. For assemblies: fails closed if any unsuppressed mate has `errorCode != 0` (or unreadable)
4. Saves
5. Force-rebuilds again and re-checks mates
6. Optionally closes/reopens (default on for assemblies), force-rebuilds, re-checks mates
7. Compares component transforms before vs after reopen (`poseStable`)

Returns `ok: true` only when save, mate health, and pose stability all pass.

## Why this exists

SolidWorks can report a soft `errorCode: 0` after `set_mate_limit_angle` / light rebuild, then surface What's Wrong on a later force rebuild or UI save. Soft `list_mates` checks alone are not a lock-in signal.

## Related

- `save_document` also force-rebuilds + validates mates for assemblies unless `skip_mate_validation: true` (recovery only).
- Prefer `confirm_and_save` for user-approved lock-in.
