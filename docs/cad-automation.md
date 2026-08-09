# CAD automation capabilities

SolidWorks MCP exposes generic document, assembly, mate, and CAD-to-URDF automation. Paths must be under `SOLIDWORKS_MCP_ALLOWED_ROOTS`.

## API docs pipeline

| Script | Purpose |
|--------|---------|
| `npm run docs:scrape:tavily` | Map and crawl help.solidworks.com |
| `npm run docs:scrape:brightdata` | Crawl through Bright Data Web Unlocker |
| `npm run docs:normalize` | Build `index.json` and `interfaces/` |
| `npm run docs:signatures` | Generate interop signatures on Windows |

MCP: `solidworks_search_api_docs`. Invoke policy: [com-invoke-abi.md](com-invoke-abi.md).

## Worker-to-MCP mapping

| Worker command | MCP tool | Notes |
|----------------|----------|-------|
| `list_configurations` | `solidworks_list_configurations` | Read |
| `set_dimension` | `solidworks_set_dimension` | Write |
| `insert_component` | `solidworks_insert_component` | Path allowlist |
| `list_interferences` | `solidworks_list_interferences` | Read |
| `get_component_transform` | `solidworks_get_component_transform` | Read |
| `set_component_transform` | `solidworks_set_component_transform` | Write |
| `mate_distance` / `mate_perpendicular` | `solidworks_mate_*` | Mate operations |
| `add_urdf_frame` | `solidworks_add_urdf_frame` | CAD-to-URDF prep |
| `get_mate_limit_angle` | `solidworks_get_mate_limit_angle` | Read limit-angle mates |
| `export_urdf_package` | `solidworks_export_urdf_package` | Manifest → CadUrdfPackage |
| _(Node)_ | `solidworks_generate_urdf` | Package → `.urdf` (no SolidWorks) |
| `invoke` / `batch_invoke` | `solidworks_invoke` / `solidworks_batch_invoke` | Allowlisted |

## CAD → package → URDF

1. Author a caller `UrdfJointManifest` (link `bodies[]`, joint tree, optional `limit_mate`).
2. Ensure each link has `urdf_link_frame` and each actuated joint has `joint_axis` (use `solidworks_urdf_readiness` / `solidworks_add_urdf_frame`; prefer `save: false` then assembly `confirm_and_save`).
3. `solidworks_export_urdf_package` with `manifest_path` + `confirm: true` writes a versioned package (JSON + visual/collision STLs).
4. `solidworks_generate_urdf` turns that package into a `.urdf`.
5. See [docs/adr/0001-cad-urdf-package.md](adr/0001-cad-urdf-package.md) for numeric contracts. The neutral example is `.demo/urdf/example-manifest.json`; golden math: `npm run test:urdf-math`.

## Selection referent (`use_selection`)

Highlight the entity you mean in SolidWorks, then pass `use_selection: true` instead of typing component, plane, or feature names.

`solidworks_resolve_selection` returns the selected entities, suggested tool arguments, and compatible commands.

## Doc acquisition fallback

1. Tavily (`tvly crawl`)
2. Bright Data (`docs:scrape:brightdata`)
3. Local CHM under `SolidWorks\api\docs\` on Windows
