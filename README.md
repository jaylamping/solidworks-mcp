# SolidWorks MCP

Windows-only MCP server for SolidWorks automation. TypeScript MCP front door; SolidWorks COM isolated in a .NET worker (STA threading).

## Repos

- `C:\code\solidworks-mcp`: MCP server and COM worker.
- `C:\code\marengo`: Mechanics + runtime; CAD under `hardware/cad/`, manifests under `hardware/manifests/`.

Open **`C:\code\marengo\marengo.code-workspace`** for both repos in one Cursor session.

## Build

```powershell
npm install
npm run build
```

Smoke check:

```powershell
npm run worker:status
npm run validate:tools
```

## API documentation corpus

Local SolidWorks API reference for LLM search:

```powershell
npm run docs:scrape:tavily      # primary (Python 3.10+ + tvly login)
npm run docs:scrape:brightdata    # fallback (BRIGHTDATA_API_TOKEN + WEB_UNLOCKER zone)
npm run docs:normalize
```

See [docs/api-reference/README.md](docs/api-reference/README.md). MCP tool: `solidworks_search_api_docs`.

Generic COM escape hatch: `solidworks_invoke` / `solidworks_batch_invoke` — see [docs/com-invoke-abi.md](docs/com-invoke-abi.md).

## Cursor MCP Config

```json
{
  "mcpServers": {
    "solidworks": {
      "command": "node",
      "args": ["C:/code/solidworks-mcp/dist/index.js"],
      "env": {
        "SOLIDWORKS_MCP_ALLOWED_ROOTS": "C:/code/marengo"
      }
    }
  }
}
```

Optional: `SOLIDWORKS_MCP_INVOKE_WRITE=true` to allow allowlisted invoke writes.

## SolidWorks read tools

| Tool | Role |
|------|------|
| `solidworks_status` | Version / active document (+ doc version warning) |
| `solidworks_measure` | Bounding box and mass properties |
| `solidworks_list_features` | Feature tree |
| `solidworks_inspect_document` | Type, path, saved, units, custom properties |
| `solidworks_list_components` | Assembly tree |
| `solidworks_list_reference_geometry` | Planes, axes, coord systems |
| `solidworks_list_bom` | Flat BOM lines |
| `solidworks_list_mates` | Mate features |
| `solidworks_list_configurations` | Part configurations |
| `solidworks_list_dimensions` | Driving dimensions |
| `solidworks_list_interferences` | Interference detection |
| `solidworks_get_component_transform` | Component 4×4 matrix |
| `solidworks_get_persist_reference` | Stable entity ID (base64) |
| `solidworks_probe_feature_faces` / `solidworks_get_feature_box` | Feature diagnostics |
| `solidworks_assembly_diagnostics` | Fixed/float/lightweight/suppressed counts |
| `solidworks_component_mass_properties` | Per-component mass/COM/inertia |
| `solidworks_search_api_docs` | Search local API markdown index |

## SolidWorks write tools

Write tools that mutate the model require **`confirm: true`** when the user explicitly requested the action.

| Tool | Role |
|------|------|
| `solidworks_open` / `solidworks_export` / `solidworks_save_document` | Document I/O |
| `solidworks_set_custom_properties` | Marengo metadata |
| `solidworks_set_dimension` | Parametric drive (**confirm**) |
| `solidworks_insert_component` | Add part to assembly (**confirm**) |
| `solidworks_set_component_transform` | Placement (**confirm**) |
| `solidworks_mate_coincident` / `parallel` / `distance` / `perpendicular` | Mates |
| `solidworks_rebuild_document` | Rebuild (**confirm**) |
| `solidworks_add_configuration_copy` | Config copy (**confirm**) |
| `solidworks_resolve_lightweight` | Resolve lightweight (**confirm**) |
| `solidworks_select_by_persist_reference` | Selection via persist ref |
| `solidworks_align_component_to_feature` | Layout alignment |
| `marengo_torso_frame_build` | Torso frame mates (**confirm**) |
| `marengo_vendor_add_rs03_urdf_frame` | URDF frame on part (**confirm**) |

## Invoke (allowlisted)

| Tool | Role |
|------|------|
| `solidworks_invoke` | Single allowlisted API call |
| `solidworks_batch_invoke` | Up to 50 calls in one worker process |

## Marengo audit tools

| Tool | Role |
|------|------|
| `marengo_cad_conventions_check` | Paths vs `cad-conventions.json` |
| `marengo_design_package_validate` | Tree vs `design-packages.json` |
| `marengo_design_review` | Combined report |
| `marengo_urdf_readiness` | URDF reference geometry |
| `marengo_kinematics_consistency` | `kinematics.md` vs assembly |
| `marengo_urdf_export_postcheck` | Exported URDF vs manifests |
| `marengo_hardware_coverage` | BOM vs vendor registry |
| `marengo_clearance_summary` | Measure + interferences + URDF gaps |
| `marengo_vendor_registry_summary` | Vendor asset registry |

Legacy aliases: `vendor_registry_summary`, `vendor_stage_local_asset`.

## Design policy

- Real-component-first vendor CAD before guessing geometry.
- Native SolidWorks files are source of truth.
- URDF export remains **manual** (Brawner); MCP audits and prepares geometry.
- Destructive actions require explicit `confirm: true` from the user request.

See [docs/cad-automation.md](docs/cad-automation.md) and [docs/troubleshooting.md](docs/troubleshooting.md).
