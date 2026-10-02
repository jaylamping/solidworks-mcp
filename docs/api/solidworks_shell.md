# solidworks_shell

Hollow the part to a wall thickness, removing the selected faces (open boxes, enclosures, covers).

| Field | Value |
|-------|-------|
| Worker command | `shell` |
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
| `thickness` | number | yes | - | Value for thickness. |
| `remove_faces` | unknown | no | - | Faces to open up (e.g. the top face). Omit for a closed hollow body. |
| `outward` | boolean | no | - | Add thickness outside the original body (default inward). |
| `name` | string | no | - | Rename the created feature. |
| `rollback_on_error` | boolean | no | - | Delete the feature again if it rebuilds with an error (default true), so the part stays clean. |

## Tags

- feature
- modeling
- shell

## Domains

- document

