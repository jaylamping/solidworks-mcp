# solidworks_split_body

Split solid bodies with a plane, face, or sketch (e.g. cut an enclosure into base and lid).

| Field | Value |
|-------|-------|
| Worker command | `split_body` |
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
| `tool` | unknown | yes | - | Plane, planar face, or sketch that cuts through the body. |
| `consume_tool` | boolean | no | - | Value for consume tool. |
| `name` | string | no | - | Rename the created feature. |
| `rollback_on_error` | boolean | no | - | Delete the feature again if it rebuilds with an error (default true), so the part stays clean. |

## Tags

- body
- modeling

## Domains

- document

