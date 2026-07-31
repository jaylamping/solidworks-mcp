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
| `add_urdf_frame` | `solidworks_add_urdf_frame` | CAD-to-URDF |
| `invoke` / `batch_invoke` | `solidworks_invoke` / `solidworks_batch_invoke` | Allowlisted |

## Selection referent (`use_selection`)

Highlight the entity you mean in SolidWorks, then pass `use_selection: true` instead of typing component, plane, or feature names.

`solidworks_resolve_selection` returns the selected entities, suggested tool arguments, and compatible commands.

## Doc acquisition fallback

1. Tavily (`tvly crawl`)
2. Bright Data (`docs:scrape:brightdata`)
3. Local CHM under `SolidWorks\api\docs\` on Windows
