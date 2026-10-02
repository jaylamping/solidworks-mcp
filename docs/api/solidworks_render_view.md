# solidworks_render_view

Orient the part (isometric/front/top/… ), zoom to fit, and save a PNG/JPG image so you can visually check the geometry.

| Field | Value |
|-------|-------|
| Worker command | `render_view` |
| Tier | core |
| Read only | false |
| Destructive | false |
| Confirm required | false |
| Description source | authored |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | yes | - | Part document to edit (.SLDPRT). Required so edits never land in whichever document happens to be active. (allowed root) |
| `output_path` | string | yes | - | PNG/JPG file under an allowed root. (allowed root) |
| `view` | `isometric`, `trimetric`, `dimetric`, `front`, `back`, `top`, `bottom`, `left`, `right`, `current` | no | - | Default isometric. |
| `width` | integer | no | - | Value for width. |
| `height` | integer | no | - | Value for height. |
| `edges` | boolean | no | - | Shaded with visible edges (default true). |
| `units` | `mm`, `cm`, `m`, `in` | no | - | Length units for every length and coordinate in this call. Default mm. |
| `bodies` | array | no | - | Show only these bodies in the image (others are hidden temporarily). |
| `section` | object | no | - | Cut-away view to inspect internal features (bores, wall thickness, pockets). Display only; removed afterwards. |

## Tags

- export
- image
- modeling

## Domains

- document

