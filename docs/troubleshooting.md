# Troubleshooting

## SolidWorks COM Object Found, API Not Responding

Observed symptom:

```json
{
  "ok": false,
  "error": "SolidWorks COM object was found, but API calls are not responding. Check SolidWorks launch state and COM/type-library registration."
}
```

This means the `SldWorks.Application` ProgID exists, but calls such as `RevisionNumber` fail. Common causes:

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
