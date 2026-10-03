# solidworks_export_link_transforms

List every component of an assembly (all levels) with its file path and placement. transform.arrayData is the 16-value SOLIDWORKS MathTransform relative to the root assembly: [0..8] are rows 0-2 of R (the component's X, Y and Z axes in assembly coordinates), [9..11] is t (the component origin, in metres), [12] is scale. A component point maps as p_asm = p_local * R + t (row vector). With output_path the same JSON is also written (indented) to that file and the result adds outputPath.

| Field | Value |
|-------|-------|
| Worker command | `export_link_transforms` |
| Tier | extended |
| Read only | false |
| Destructive | false |
| Confirm required | false |
| Description source | authored |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | yes | - | Assembly (.SLDASM) to read. (allowed root) |
| `output_path` | string | no | - | Also write the result as indented JSON to this file; parent folders are created and an existing file is overwritten. (allowed root) |

## Tags

- export

## Domains

- document

