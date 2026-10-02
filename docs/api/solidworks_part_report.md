# solidworks_part_report

Read-only part summary: feature tree with rebuild errors, sketches, solid bodies with volume and bounding box, and optionally every face/edge with geometry (filterable) so you can build face/edge selectors. Run after each change to verify the model.

| Field | Value |
|-------|-------|
| Worker command | `part_report` |
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
| `include_faces` | boolean | no | - | List every face with surface type, center, normal/axis, area. |
| `include_edges` | boolean | no | - | List every edge with type, endpoints, midpoint, radius. |
| `face_filter` | object | no | - | Only list faces matching these selector filters. |
| `edge_filter` | object | no | - | Only list edges matching these selector filters. |
| `max_items` | integer | no | - | Cap per list (default 200). |

## Tags

- inspect
- modeling

## Domains

- document

