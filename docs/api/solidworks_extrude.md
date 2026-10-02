# solidworks_extrude

Extrude a sketch as a boss (add) or cut (remove): blind, through_all, up_to_next, up_to_face, offset_from_face, or mid_plane; optional second direction, draft, thin wall, start offset, separate body. Returns the new feature plus updated volume/bounding box; rolls back if it rebuilds with an error.

| Field | Value |
|-------|-------|
| Worker command | `extrude` |
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
| `sketch` | string | yes | - | Sketch (closed profile) to extrude. |
| `mode` | `boss`, `cut` | no | - | boss adds material (default), cut removes it. |
| `end` | `blind`, `through_all`, `up_to_next`, `up_to_face`, `offset_from_face`, `mid_plane` | no | - | Default blind. |
| `depth` | number | no | - | Depth for blind/mid_plane, offset for offset_from_face. |
| `up_to` | unknown | no | - | Face/plane for up_to_face / offset_from_face. |
| `reverse` | boolean | no | - | Flip the extrude direction (default: along the sketch normal; cuts go into the material). |
| `direction2` | object | no | - | Also extrude in the opposite direction. |
| `draft_deg` | number | no | - | Value for draft deg. |
| `draft_outward` | boolean | no | - | Value for draft outward. |
| `thin` | object | no | - | Thin-feature extrude (wall of given thickness along open or closed contours). |
| `start_offset` | number | no | - | Start the extrude offset from the sketch plane. |
| `merge` | boolean | no | - | Boss only: merge into existing bodies (default true); false makes a separate body. |
| `scope_bodies` | array | no | - | Limit a cut / merge to these body names. |
| `name` | string | no | - | Rename the created feature. |
| `rollback_on_error` | boolean | no | - | Delete the feature again if it rebuilds with an error (default true), so the part stays clean. |

## Tags

- feature
- modeling
- extrude
- cut

## Domains

- document

