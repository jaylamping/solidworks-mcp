# CAD automation capabilities

SolidWorks MCP worker commands exposed via TypeScript tools. Paths must be under `SOLIDWORKS_MCP_ALLOWED_ROOTS` (default: Marengo). Worker also enforces allowlist on `part_path` for `insert_component`.

## API docs pipeline

| Script | Purpose |
|--------|---------|
| `npm run docs:scrape:tavily` | Map + crawl help.solidworks.com |
| `npm run docs:scrape:brightdata` | BFS via Bright Data Web Unlocker |
| `npm run docs:normalize` | Build `index.json` + `interfaces/` |
| `npm run docs:signatures` | Interop signatures (Windows + SW install) |

MCP: `solidworks_search_api_docs`. Invoke policy: [com-invoke-abi.md](com-invoke-abi.md).

## Layout-driven frame build

**Tool:** `marengo_torso_frame_build`  
**Worker:** `torso_frame_build_mates`

See prior sections in git history for torso pipeline steps. Manual scripts still require `--confirm`.

## Worker ↔ MCP mapping (humanoid-focused)

| Worker command | MCP tool | Notes |
|----------------|----------|-------|
| `list_configurations` | `solidworks_list_configurations` | read |
| `list_dimensions` | `solidworks_list_dimensions` | read |
| `set_dimension` | `solidworks_set_dimension` | confirm |
| `insert_component` | `solidworks_insert_component` | confirm; path allowlist |
| `list_interferences` | `solidworks_list_interferences` | read |
| `get_component_transform` | `solidworks_get_component_transform` | read |
| `set_component_transform` | `solidworks_set_component_transform` | confirm |
| `mate_distance` / `mate_perpendicular` | `solidworks_mate_*` | confirm; AddMate5 quirk |
| `rebuild_document` | `solidworks_rebuild_document` | confirm |
| `get_persist_reference` | `solidworks_get_persist_reference` | identity for invoke |
| `select_by_persist_reference` | `solidworks_select_by_persist_reference` | |
| `add_configuration_copy` | `solidworks_add_configuration_copy` | confirm |
| `vendor_add_rs03_urdf_frame` | `marengo_vendor_add_rs03_urdf_frame` | confirm |
| `assembly_diagnostics` | `solidworks_assembly_diagnostics` | read |
| `component_mass_properties` | `solidworks_component_mass_properties` | read |
| `resolve_lightweight` | `solidworks_resolve_lightweight` | confirm |
| `invoke` / `batch_invoke` | `solidworks_invoke` / `solidworks_batch_invoke` | allowlisted |

## Marengo clearance

**Tool:** `marengo_clearance_summary` — combines `measure`, `list_interferences`, and `marengo_urdf_readiness` (static pose only).

## Known limitation

`AddMate5` often returns `mateCreated: false` with valid selections. Verify mates in the tree; frame build may fix-in-place after ICE alignment.

## Doc acquisition fallback

1. Tavily (`tvly crawl`)  
2. Bright Data (`docs:scrape:brightdata`)  
3. Local CHM under `SolidWorks\api\docs\` on Windows

## Selection referent (`use_selection`)

Highlight the entity you mean in SolidWorks, then pass **`use_selection: true`** instead of typing `component_name`, `plane_name`, `feature_name`, etc.

| Step | Tool |
|------|------|
| Inspect highlight | `marengo_resolve_selection` or `solidworks_resolve_selection` |
| Use highlight as target | Any supported worker/MCP tool + `use_selection: true` |

**Multi-select:** SolidWorks order is index `1`, then `2` (mates, align, cutouts). For single-target tools, pass `selection_index` to pick which highlight you mean.

**`resolve_selection` returns:** `primary`, `selections[]` (each with `label`, `kind`, `toolArgs`, `semanticTags`), plus `hints.selectionAwareCommands`.

**Examples:** `get_component_box`, `create_sketch`, `mate_coincident`, `actuator_mount_hole_pattern`, `actuator_cut_cavity` — all accept `use_selection`. Worker injects `path` from the active document when omitted.

## Shoulder pitch bracket iteration

**Probe (read-only):** `marengo_shoulder_bracket_probe`  
Bundles RS03 envelope, `shoulder_mount_*` layout refs, motor instance boxes/transforms, and starter block dims from `hardware/docs/torso-actuator-brackets.md`. Returns `partialErrors` when sub-probes fail (motor not placed, SW down, etc.).

**Selection-aware cutout**

1. In SolidWorks, highlight the **bracket body/face/component** (and optionally the **actuator** instance).
2. `marengo_resolve_selection` — inspect `primary.toolArgs` / `semanticTags`.
3. `marengo_actuator_cut_cavity` with `use_selection: true`, `confirm: true` — cavity cut without typing component names.

`path` optional when the target assembly is the active document. Pass `model: rs03` if only the bracket is highlighted.

## Next extensions
