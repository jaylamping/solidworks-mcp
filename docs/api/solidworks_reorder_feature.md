# solidworks_reorder_feature

Move a feature before/after another feature (or to the end) in the tree.

| Field | Value |
|-------|-------|
| Worker command | `reorder_feature` |
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
| `feature` | string | yes | - | Value for feature. |
| `before` | string | no | - | Value for before. |
| `after` | string | no | - | Value for after. |

## Tags

- edit
- modeling

## Domains

- document

