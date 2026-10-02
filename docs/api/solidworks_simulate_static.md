# solidworks_simulate_static

Linear static FEA (SOLIDWORKS Simulation) on a part: material (printed PLA/PETG/ABS/ASA/PC/CF-nylon, aluminum, steel, library or custom), fixtures and forces/pressures on selector-chosen faces, mesh, solve; returns max von Mises stress and where, max displacement and where, factor of safety, mesh size. The study is removed afterwards unless keep_study. Use it to check and compare design changes for strength and stiffness.

| Field | Value |
|-------|-------|
| Worker command | `simulate_static` |
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
| `material` | string | no | - | Built-in: PLA (default), PETG, ABS, ASA, PC, PA-CF, PETG-CF, 6061-T6, 7075-T6, STEEL, STAINLESS; or a SOLIDWORKS library material name (e.g. "AISI 1020"). |
| `custom_material` | object | no | - | Value for custom material. |
| `fixtures` | object[] | yes | - | Supports. fixed = all DOF; roller = slides in-plane; hinge = rotates about a cylindrical face's axis. |
| `loads` | object[] | yes | - | Value for loads. |
| `gravity` | boolean | no | - | Add self-weight along -Y. |
| `mesh_quality` | `draft`, `high` | no | - | Value for mesh quality. |
| `element_size` | number | no | - | Global element size in `units` (default: SOLIDWORKS default). |
| `bodies` | array | no | - | Analyze only these bodies (e.g. the bracket in a part that also contains an imported actuator). Others are excluded via a temporary Keep Body feature that is removed afterwards. |
| `keep_study` | boolean | no | - | Leave the study in the part for inspection in the UI (default: delete it). |
| `study_name` | string | no | - | Value for study name. |

## Tags

- analysis
- fea
- simulation
- strength

## Domains

- document

