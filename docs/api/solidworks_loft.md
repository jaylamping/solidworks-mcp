# solidworks_loft

Loft between two or more profiles (sketches on different planes, faces) as a boss or cut, optionally with guide curves.

| Field | Value |
|-------|-------|
| Worker command | `loft` |
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
| `profiles` | array | yes | - | Ordered profiles: sketch names, planar faces, or points (first/last only). |
| `guides` | array | no | - | Guide-curve sketches/edges. |
| `mode` | `boss`, `cut` | no | - | Value for mode. |
| `closed` | boolean | no | - | Close the loft back to the first profile. |
| `merge` | boolean | no | - | Value for merge. |
| `name` | string | no | - | Rename the created feature. |
| `rollback_on_error` | boolean | no | - | Delete the feature again if it rebuilds with an error (default true), so the part stays clean. |

## Tags

- feature
- modeling
- loft

## Domains

- document

