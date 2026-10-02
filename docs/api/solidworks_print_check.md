# solidworks_print_check

FDM printability: for each axis-aligned build direction, unsupported overhang area, flat down-facing (bridge) area, bed contact area, height, footprint and bed fit; recommends the best orientation; solid mass and filament length for a material.

| Field | Value |
|-------|-------|
| Worker command | `print_check` |
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
| `material` | string | no | - | Filament for mass: PLA (default), PETG, ABS, ASA, TPU, PA/NYLON, PA-CF, PETG-CF, PC. |
| `density_g_cm3` | number | no | - | Density for unknown materials. |
| `overhang_deg` | number | no | - | Max printable overhang from vertical (default 45). |
| `bed` | number[] | no | - | Printer build volume [x, y, z] in `units` (default 256x256x256 mm, Bambu X1/P1). |
| `up` | number[] | no | - | Also evaluate this exact build direction (model vector pointing up). |

## Tags

- inspect
- analysis
- 3d-printing

## Domains

- document

