# Troubleshooting

## SolidWorks COM Object Found, API Not Responding

Observed symptom:

```json
{
  "ok": false,
  "error": "SolidWorks COM object was found, but API calls are not responding. Check SolidWorks launch state and COM/type-library registration."
}
```

This means the `SldWorks.Application` ProgID exists, but dynamic COM calls such as `RevisionNumber` fail. Common causes:

- SolidWorks is not actually running in the current desktop session.
- SolidWorks COM/type-library registration is stale or incomplete.
- SolidWorks was installed but not launched once interactively.
- Windows has a stale Running Object Table entry.

Checks:

```powershell
Get-Process SLDWORKS -ErrorAction SilentlyContinue
Get-ItemProperty "Registry::HKEY_CLASSES_ROOT\SldWorks.Application\CLSID"
```

Manual repair path:

1. Launch SolidWorks normally once from Start Menu.
2. Close it cleanly.
3. Retry `npm run worker:status` or the `solidworks_status` MCP tool.
4. If still broken, repair/re-register SolidWorks COM from the installed SolidWorks tools or installer.

Do not design from guessed actuator dimensions while this is broken. Use staged STEP files and mark native import as pending.

## Stale MCP server after `npm run build`

Symptom: tools respond but omit new fields (e.g. `marengo_urdf_readiness` missing `scopesScanned` / `referenceLocations`), or `solidworks_status` has no `mcpVersion`.

Cursor keeps the MCP Node process alive across builds. Fix:

1. Run `npm run build` in `solidworks-mcp`.
2. Restart the SolidWorks MCP server in Cursor (MCP settings → restart) or **Developer: Reload Window**.
3. Confirm with `solidworks_status` — expect `mcpVersion: "0.2.0"` (or current package version).
4. Optional smoke test: `npm run validate:tools` (SolidWorks must be running).

Resolved local path:

- Dynamic COM dispatch failed with `TYPE_E_ELEMENTNOTFOUND`.
- Typed SolidWorks .NET interop from `C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist` works.
- Worker references `SolidWorks.Interop.sldworks.dll` and `SolidWorks.Interop.swconst.dll` directly and copies them locally at build time.

## STEP/STP Import

Do not use `OpenDoc6` for neutral CAD imports. SolidWorks can return `swFileRequiresRepairError` for valid STEP files.

Use:

- `ISldWorks.GetImportFileData(path)`
- `ISldWorks.LoadFile4(path, "r", importData, ref errors)`

Then save as `.SLDPRT` with `ModelDocExtension.SaveAs`.
