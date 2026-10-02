# solidworks_combine

Boolean bodies: add (union), subtract, or common (intersection). Bodies by name or {near:[x,y,z]}.

| Field | Value |
|-------|-------|
| Worker command | `combine` |
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
| `operation` | `add`, `subtract`, `common` | yes | - | Value for operation. |
| `target` | array | yes | - | The body to keep/modify (one). |
| `tools` | array | yes | - | Bodies added to / subtracted from / intersected with the target. |
| `name` | string | no | - | Rename the created feature. |
| `rollback_on_error` | boolean | no | - | Delete the feature again if it rebuilds with an error (default true), so the part stays clean. |

## Tags

- body
- modeling
- boolean

## Domains

- document

