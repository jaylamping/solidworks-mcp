# solidworks_demo_build_part

Build the deterministic demo part: 40 mm cube with Ø18 mm through-cylinders on Front, Top, and Right (confirm: true). Prefer output_path under the clone's .demo/DemoCube.SLDPRT when allowed. CLI equivalent: npm run demo:build-part.

| Field | Value |
|-------|-------|
| Worker command | `demo_build_part` |
| Tier | core |
| Read only | false |
| Destructive | true |
| Confirm required | true |
| Description source | authored |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `size_mm` | number | yes | `40` | Cube edge length in millimeters. |
| `hole_diameter_mm` | number | yes | `18` | Diameter of the through-cylinders cut on each axis. Must be smaller than size_mm. |
| `output_path` | string | no | - | Value for output path. (allowed root) |
| `confirm` | const `true` | yes | `true` | Value for confirm. |

## Tags

- demo
- part
- modeling

## Domains

- document

