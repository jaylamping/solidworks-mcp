# solidworks_ref_plane

Create a reference plane offset from a plane/face, angled about an axis/edge, or through three points.

| Field | Value |
|-------|-------|
| Worker command | `ref_plane` |
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
| `from` | unknown | no | - | Base plane or planar face (not needed with through). |
| `offset` | number | no | - | Offset distance along the base normal (negative flips). |
| `angle_deg` | number | no | - | Rotate about `about` by this angle instead of offsetting. |
| `about` | unknown | no | - | Axis / linear edge for an angled plane. |
| `through` | array | no | - | Alternative: plane through 3 vertices/points. |
| `name` | string | no | - | Rename the created feature. |

## Tags

- reference
- modeling
- plane

## Domains

- document

