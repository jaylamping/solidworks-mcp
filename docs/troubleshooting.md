# Troubleshooting

## SolidWorks crashes during MCP audits

Observed when multiple MCP tools or scripts hit SolidWorks at once (for example parallel `list_components` and `solidworks_open`).

Symptoms:

- SolidWorks dialog: "encountered a problem and needs to close"
- MCP error: `The remote procedure call failed. (0x800706BE)`
- Partial tool output (null `document`, truncated component lists)

Root cause:

- SolidWorks COM is **STA and single-process**. Each MCP tool spawns a .NET worker that attaches via COM.
- **Concurrent workers** re-open assemblies and traverse the feature tree at the same time → SolidWorks crash.
- **`dotnet run` per call** can also race MSBuild if workers start while a build is in progress.

Fix (solidworks-mcp ≥ 0.3.1):

1. Workers are **serialized** in the MCP server (`worker.ts` queue).
2. A **global named mutex** in the .NET worker blocks cross-process COM overlap (MCP + scripts).
3. **`OpenDocument` reuses** already-open files instead of calling `OpenDoc6` again.

After pulling the fix:

```powershell
cd C:\code\solidworks-mcp
npm run build
# Restart SolidWorks MCP in Cursor
```

Agent rule: **never parallelize SolidWorks MCP tool calls** against the same session. Run audits sequentially even with the lock — it keeps SW responsive while you model.

If SolidWorks still crashes, check `%LOCALAPPDATA%\CrashDumps\SLDWORKS*.dmp` and Windows Event Viewer → Application for `SLDWORKS.exe`.

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

Do not design from guessed dimensions while this is broken. Use staged STEP files and mark native import as pending.

## Stale MCP server after `npm run build`

Symptom: tools respond but omit new fields, or `solidworks_status` has no `mcpVersion`.

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
