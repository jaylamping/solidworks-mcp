# SolidWorks MCP

Windows-only MCP server for SolidWorks automation. The MCP front door is TypeScript; SolidWorks COM calls are isolated in a .NET worker so we can own STA threading and avoid Node native COM module fragility.

## Repos

- `C:\code\solidworks-mcp`: MCP server and automation tooling.
- `C:\code\robot-cad`: Git LFS CAD vault for source CAD, vendor CAD, exports, and manifests.
- `C:\code\rudy`: downstream ROS/software consumer.

## Build

```powershell
npm install
npm run build
```

Smoke check:

```powershell
npm run worker:status
```

If COM is registered but not responsive, see `docs/troubleshooting.md`.

## Cursor MCP Config

Use a user or workspace MCP config entry like:

```json
{
  "mcpServers": {
    "solidworks": {
      "command": "node",
      "args": ["C:/code/solidworks-mcp/dist/index.js"],
      "env": {
        "SOLIDWORKS_MCP_ALLOWED_ROOTS": "C:/code/robot-cad;C:/Users/joeyl/OneDrive/Desktop/robot"
      }
    }
  }
}
```

## Initial Tools

- `vendor_registry_summary`: summarize vendor CAD assets and import readiness.
- `vendor_stage_local_asset`: copy local vendor CAD into the CAD vault and record checksums; defaults to dry run.
- `solidworks_status`: attach to running SolidWorks and report active document metadata.
- `solidworks_open`: open `.SLDPRT`, `.SLDASM`, `.SLDDRW`, `.STEP`, or `.STP` from allowed CAD roots.
- `solidworks_export`: export active or specified document to `STEP`, `STL`, `PDF`, or `PNG`.
- `solidworks_measure`: collect bounding box and mass-property metadata.
- `solidworks_list_features`: list top-level feature tree entries.

## Vendor CAD Workflow

1. Add or update an entry in `C:\code\robot-cad\manifests\vendor-assets.json`.
2. Run `vendor_stage_local_asset` for local files, or add a downloader/fetcher for trusted supplier URLs.
3. Import the staged STEP/STP into SolidWorks.
4. Add named reference geometry: axes, mounting faces, cable exits, keepouts, and tool access.
5. Update registry `cad.nativePath` once the SolidWorks part is saved.

## Design Policy

- Real-component-first: import vendor CAD for RobStride actuators, fasteners, sensors, Raspberry Pi boards, bearings, inserts, connectors, and extrusions before designing around them.
- Native SolidWorks parts/assemblies are source of truth.
- Use `STEP` for neutral solid exchange and `STL`/`GLB` only for mesh consumers.
- Do not design from guessed dimensions when vendor geometry exists.
- Placeholders must be labeled in manifests with owner, date, and replacement target.
