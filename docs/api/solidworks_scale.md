# solidworks_scale

Scale feature: uniform (factor) or per-axis ([fx, fy, fz]) scaling of all or selected bodies about their centroid or the part origin. Use for shrinkage compensation (e.g. 1.006 for ASA/ABS) or resizing a concept.

| Field | Value |
|-------|-------|
| Worker command | `scale` |
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
| `factor` | unknown | yes | - | Uniform factor, or [fx, fy, fz] per model axis. |
| `about` | `centroid`, `origin` | no | - | Scale point (default centroid). |
| `bodies` | array | no | - | Bodies to scale (default all). |
| `name` | string | no | - | Rename the created feature. |
| `rollback_on_error` | boolean | no | - | Delete the feature again if it rebuilds with an error (default true), so the part stays clean. |

## Tags

- modeling
- feature
- body

## Domains

- document

