# solidworks_ref_axis

Create a reference axis from a cylindrical face, a linear edge, two planes, or two points.

| Field | Value |
|-------|-------|
| Worker command | `ref_axis` |
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
| `from` | array | yes | - | One cylindrical/conical face or linear edge; or two planes (intersection); or two vertices/points. |
| `name` | string | no | - | Rename the created feature. |

## Tags

- reference
- modeling
- axis

## Domains

- document

