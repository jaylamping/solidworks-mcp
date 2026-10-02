# solidworks_sheet_metal_base_flange

Sheet-metal base flange from a sketch: an open profile (polyline/lines) becomes a bent bracket or channel with a bend at every corner, extruded by depth; a closed profile becomes a flat tab. Sets thickness, bend radius and K-factor. Add holes with solidworks_extrude cuts or solidworks_hole, then solidworks_flat_pattern for the DXF.

| Field | Value |
|-------|-------|
| Worker command | `sheet_metal_base_flange` |
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
| `sketch` | string | yes | - | Profile sketch: open (bent bracket/channel, needs depth) or closed (flat tab). |
| `thickness` | number | yes | - | Sheet thickness in `units`. |
| `bend_radius` | number | no | - | Inside bend radius (default = thickness). |
| `depth` | number | no | - | Extrusion depth of an open profile. |
| `mid_plane` | boolean | no | - | Extrude an open profile symmetrically about the sketch plane. |
| `reverse` | boolean | no | - | Extrude an open profile the other way. |
| `reverse_thickness` | boolean | no | - | Put the thickness on the other side of the profile line. |
| `k_factor` | number | no | - | Bend K-factor (default from the part, typically 0.5). |
| `name` | string | no | - | Rename the created feature. |
| `rollback_on_error` | boolean | no | - | Delete the feature again if it rebuilds with an error (default true), so the part stays clean. |

## Tags

- modeling
- feature
- sheet-metal

## Domains

- document

