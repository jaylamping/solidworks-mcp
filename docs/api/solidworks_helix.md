# solidworks_helix

Helix/spiral curve from a base circle (pitch + height or revolutions). Sweep a profile along it with solidworks_sweep (sweep_path = the helix) for threads, springs, augers.

| Field | Value |
|-------|-------|
| Worker command | `helix` |
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
| `on` | unknown | yes | - | Plane or planar face for the base circle. |
| `center` | number[] | no | - | Base circle center (sketch coords, or model with space:"model"). |
| `space` | `sketch`, `model` | no | - | Value for space. |
| `diameter` | number | yes | - | Value for diameter. |
| `pitch` | number | yes | - | Value for pitch. |
| `height` | number | no | - | Height (with pitch). Or give revolutions. |
| `revolutions` | number | no | - | Value for revolutions. |
| `clockwise` | boolean | no | - | Value for clockwise. |
| `reverse` | boolean | no | - | Value for reverse. |
| `start_angle_deg` | number | no | - | Value for start angle deg. |
| `taper_deg` | number | no | - | Value for taper deg. |
| `taper_outward` | boolean | no | - | Value for taper outward. |
| `name` | string | no | - | Rename the created feature. |
| `rollback_on_error` | boolean | no | - | Delete the feature again if it rebuilds with an error (default true), so the part stays clean. |

## Tags

- feature
- modeling
- helix
- thread

## Domains

- document

