# solidworks_hole

Drill one or more holes from a planar face: plain, counterbored, or countersunk; blind or through-all. Positions are model points by default. Good for screw clearance, heat-set insert, and bearing holes.

| Field | Value |
|-------|-------|
| Worker command | `hole` |
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
| `on` | unknown | yes | - | Planar face or plane the holes start from. Holes go into the material (normal opposite the face's outward normal). |
| `positions` | array[] | yes | - | Hole centers. 2D sketch coords on the face, or 3D model points with space:"model". |
| `space` | `sketch`, `model` | no | - | Coordinate space for positions (default model for faces, since face sketch axes are not obvious). |
| `diameter` | number | yes | - | Value for diameter. |
| `depth` | number | no | - | Omit for through-all. |
| `counterbore` | object | no | - | Value for counterbore. |
| `countersink` | object | no | - | Countersink head diameter at the surface; angle default 90 (metric flat-head). |
| `name` | string | no | - | Rename the created feature. |
| `rollback_on_error` | boolean | no | - | Delete the feature again if it rebuilds with an error (default true), so the part stays clean. |

## Tags

- feature
- modeling
- hole

## Domains

- document

