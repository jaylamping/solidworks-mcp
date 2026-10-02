# solidworks_fillet

Constant-radius fillet on edges chosen by selector (nearest-point, filters, or all edges of a face). No prior UI selection needed.

| Field | Value |
|-------|-------|
| Worker command | `fillet` |
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
| `radius` | number | yes | - | Value for radius. |
| `edges` | unknown | yes | - | Edges to round. A face selector rounds all of that face's edges. Use {edges:{...filters}} to grab many at once. |
| `name` | string | no | - | Rename the created feature. |
| `rollback_on_error` | boolean | no | - | Delete the feature again if it rebuilds with an error (default true), so the part stays clean. |

## Tags

- feature
- modeling
- fillet

## Domains

- document

