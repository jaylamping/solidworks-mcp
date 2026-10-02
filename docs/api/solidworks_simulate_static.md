# solidworks_simulate_static

FEA (SOLIDWORKS Simulation) on a part. Static: material (printed PLA/PETG/ABS/ASA/PC/CF-nylon, aluminum, steel, library or custom), fixtures, and loads on selector-chosen faces (force, pressure, torque, remote force/moment acting at a point such as a lever end or actuator centre); returns peak von Mises stress and displacement with locations, factor of safety, stress percentiles, the peak away from fixture singularities, per-face probes, optional hot-spot diagnostics and a contour plot image. analysis=frequency: natural frequencies with mass participation and a mode-shape image. The study is removed afterwards unless keep_study. Use it to check and compare design changes for strength, stiffness and vibration.

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
| `analysis` | `static`, `frequency`, `topology` | no | - | static (default): stress/displacement under loads. frequency: natural frequencies and mode shapes (loads optional; no fixtures = free-free). topology: topology optimization — which material carries the loads and which can be removed (see `topology`). |
| `topology` | object | no | - | Topology optimization settings (analysis = topology). |
| `modes` | integer | no | - | Frequency analysis: number of modes (default 5). |
| `fixtures` | object[] | no | - | Supports (required for static). fixed = all DOF; roller = slides in-plane; hinge = rotates about a cylindrical face's axis. |
| `loads` | object[] | no | - | Loads (required for static). |
| `probes` | object[] | no | - | Report von Mises stress on these faces (max, mean and where), e.g. a fillet you are sizing. Peaks at fixture edges and sharp corners are mesh singularities; probes on the region you care about are the reliable comparison. |
| `singularity_exclusion` | number | no | - | distribution.awayFromFixtures ignores nodes within this distance of fixture faces (default 2 element sizes). |
| `build_direction` | number[] | no | - | FDM layer check: the print's up direction (model vector). Reports tension across the layers vs interlayer strength. |
| `layer_strength_factor` | number | no | - | Interlayer strength as a fraction of the material strength (default 0.6). |
| `hotspots` | boolean | no | - | Run SOLIDWORKS stress hot-spot diagnostics and list hot-spot locations (singular or genuinely concentrated). |
| `plot` | object | no | - | Save a result plot image (stress contour by default). |
| `gravity` | boolean | no | - | Add self-weight along -Y. |
| `mesh_quality` | `draft`, `high` | no | - | Value for mesh quality. |
| `element_size` | number | no | - | Global element size in `units` (default: SOLIDWORKS default). |
| `mesh_controls` | object[] | no | - | Local mesh refinement on the faces whose stress matters (fillets, notches, holes) — use with probes for converged values. |
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

