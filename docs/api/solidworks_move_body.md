# solidworks_move_body

Translate and/or rotate solid bodies (optionally as a copy).

| Field | Value |
|-------|-------|
| Worker command | `move_body` |
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
| `bodies` | array | yes | - | Value for bodies. |
| `translate` | number[] | no | - | Value for translate. |
| `rotate` | object | no | - | Rotation about an axis through axis_point. |
| `copy` | boolean | no | - | Keep the original and move a copy. |
| `name` | string | no | - | Rename the created feature. |

## Tags

- body
- modeling

## Domains

- document

