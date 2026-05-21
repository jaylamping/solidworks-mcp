# SolidWorks MCP

Windows-only MCP server for SolidWorks automation. The MCP front door is TypeScript; SolidWorks COM calls are isolated in a .NET worker so we can own STA threading and avoid Node native COM module fragility.

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
```

If COM is registered but not responsive, see `docs/troubleshooting.md`.

## Cursor MCP Config

Workspace config lives in marengo:

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

Optional override: `MARENGO_ROOT=C:/code/marengo`.

## SolidWorks tools

| Tool | Role |
|------|------|
| `solidworks_status` | Attach and report version / active document |
| `solidworks_open` | Open `.SLDPRT`, `.SLDASM`, `.STEP`, `.STP` from allowed roots |
| `solidworks_export` | Export STEP, STL, PDF, PNG |
| `solidworks_measure` | Bounding box and mass properties |
| `solidworks_list_features` | Top-level feature tree |
| `solidworks_inspect_document` | Type, path, saved, units, custom properties |
| `solidworks_list_components` | Assembly tree |
| `solidworks_list_reference_geometry` | Named planes, axes, coord systems |
| `solidworks_list_bom` | Flat assembly component list |

## Marengo audit tools (read-only)

| Tool | Role |
|------|------|
| `marengo_cad_conventions_check` | Filename/layout vs `cad-conventions.json` |
| `marengo_design_package_validate` | Assembly tree vs `design-packages.json` |
| `marengo_design_review` | Combined report + checklist |
| `marengo_urdf_readiness` | URDF reference geometry before Brawner export; walks assembly component trees |
| `marengo_kinematics_consistency` | `kinematics.md` vs assembly instance names |
| `marengo_urdf_export_postcheck` | Exported URDF vs kinematics + `config/motors.yaml` |
| `marengo_vendor_registry_summary` | Vendor registry under `hardware/manifests/` |
| `marengo_hardware_coverage` | Assembly BOM vs registry + `master-bom.csv` |

Legacy aliases: `vendor_registry_summary`, `vendor_stage_local_asset` (default registry path is Marengo).

## Vendor CAD workflow

1. Add or update `C:\code\marengo\hardware\manifests\vendor-assets.json`.
2. Stage STEP under `hardware/cad/vendor/`.
3. Import in SolidWorks; add named reference geometry per `hardware/docs/cad-standards.md`.
4. Run `marengo_design_review` before saving assemblies.

## Design policy

- Real-component-first: vendor CAD for actuators, fasteners, boards, bearings before designing around guesses.
- Native SolidWorks parts/assemblies are source of truth.
- URDF export is **manual** (Brawner) → `assets/urdf/marengo.urdf`; MCP audits only.
- MCP does not auto-mate, insert components, move features, or write URDF yet.
