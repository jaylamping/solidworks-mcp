# solidworks_measure_clearance

Clearance between two sets of bodies or faces in a part: minimum distance with the closest points on each side, whether bodies overlap and the interference volume (computed on temporary copies; the part is not changed), and pass/fail against min_clearance. Use it to check a new bracket against an inserted actuator, a lid against its base, or moving parts against each other.

| Field | Value |
|-------|-------|
| Worker command | `measure_clearance` |
| Tier | core |
| Read only | true |
| Destructive | false |
| Confirm required | false |
| Description source | authored |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | yes | - | Part document to edit (.SLDPRT). Required so edits never land in whichever document happens to be active. (allowed root) |
| `units` | `mm`, `cm`, `m`, `in` | no | - | Length units for every length and coordinate in this call. Default mm. |
| `a` | object | yes | - | One side of the check: bodies or faces. |
| `b` | object | yes | - | One side of the check: bodies or faces. |
| `min_clearance` | number | no | - | Required gap in `units`; the result reports pass/fail. |

## Tags

- analysis
- measure
- interference
- clearance

## Domains

- document

