# solidworks_try_variants

Design exploration: for each variant (dimension changes and/or extra modeling steps such as a fillet, rib or thicker wall) apply it, measure volume/mass, FEA (same fixtures and loads for all) and optionally wall thickness, then undo it. Returns a side-by-side table and confirms the part was restored. Use it to compare candidate improvements before committing to one.

| Field | Value |
|-------|-------|
| Worker command | `try_variants` |
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
| `variants` | object[] | yes | - | Value for variants. |
| `analysis` | object | no | - | solidworks_simulate_static arguments (material, bodies, fixtures, loads, mesh_quality...) run identically on every variant. |
| `thickness` | boolean | no | - | Also report thinnest / 5th-percentile / median wall per variant. |
| `density_g_cm3` | number | no | - | Mass density for massGrams (defaults from the analysis material). |
| `include_baseline` | boolean | no | - | Measure the unmodified part first (default true). |

## Tags

- analysis
- fea
- design
- optimization

## Domains

- document

