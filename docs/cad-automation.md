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
