# solidworks_export

Export a CAD document to STEP, STL, PDF, or PNG under an allowed CAD root. PNG supports keep_view for stable before/after screenshots.

| Field | Value |
|-------|-------|
| Worker command | `export` |
| Tier | core |
| Read only | false |
| Destructive | false |
| Confirm required | false |
| Description source | authored |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | no | - | Optional source document path under an allowed CAD root. (allowed root) |
| `output_path` | string | yes | - | Destination file path under an allowed CAD root. (allowed root) |
| `format` | `sldprt`, `sldasm`, `step`, `stp`, `stl`, `pdf`, `png` | yes | - | Output format. |
| `start_if_missing` | boolean | no | - | Start SolidWorks if no instance is running. |
| `keep_view` | boolean | no | - | For png/jpg previews: keep the current camera/view instead of forcing isometric + zoom-to-fit. Use this for before/after reasoning screenshots. |

## Tags

- export

## Domains

- document

