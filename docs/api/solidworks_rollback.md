# solidworks_rollback

Move the rollback bar before/after a feature so new features are inserted mid-tree (e.g. add a fillet before a shell); call with no before/after to roll forward to the end.

| Field | Value |
|-------|-------|
| Worker command | `rollback` |
| Tier | core |
| Read only | false |
| Destructive | false |
| Confirm required | false |
| Description source | authored |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | yes | - | Part document to edit (.SLDPRT). Required so edits never land in whichever document happens to be active. (allowed root) |
| `units` | `mm`, `cm`, `m`, `in` | no | - | Length units for every length and coordinate in this call. Default mm. |
| `before` | string | no | - | Roll back to just before this feature (new features are inserted there). |
| `after` | string | no | - | Roll back to just after this feature. |

## Tags

- edit
- modeling

## Domains

- document

