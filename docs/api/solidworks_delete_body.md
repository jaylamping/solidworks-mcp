# solidworks_delete_body

Delete solid bodies by name (adds a Delete/Keep Body feature).

| Field | Value |
|-------|-------|
| Worker command | `delete_body` |
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
| `name` | string | no | - | Rename the created feature. |

## Tags

- body
- modeling

## Domains

- document

