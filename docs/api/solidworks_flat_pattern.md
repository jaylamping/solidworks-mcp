# solidworks_flat_pattern

Sheet-metal flat pattern: unfolded blank size (length, width, thickness), bend count, and optional DXF export with bend lines for laser/waterjet cutting. The part is folded again afterwards.

| Field | Value |
|-------|-------|
| Worker command | `flat_pattern` |
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
| `output_path` | string | no | - | DXF file to write (flat pattern geometry). (allowed root) |
| `bend_lines` | boolean | no | - | Include bend lines in the DXF (default true). |

## Tags

- sheet-metal
- export
- analysis

## Domains

- document

