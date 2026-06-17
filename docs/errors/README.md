# SolidWorks MCP error catalog

Structured errors flow from the .NET worker (`WorkerError`) through `src/errors.ts` into MCP tool responses.

## Categories

| Category | Source |
|----------|--------|
| `validation` | Path guards, missing args, confirm gates |
| `worker` | Worker orchestration, unknown commands |
| `com` | HRESULT / RPC / ROT failures |
| `solidworks` | SW out-params (`mateError`, load/save flags) |

## Common codes

| Code | Meaning | Remediation |
|------|---------|-------------|
| `PATH_NOT_ALLOWED` | Path outside `SOLIDWORKS_MCP_ALLOWED_ROOTS` | Set env to include your CAD root |
| `CONFIRM_REQUIRED` | Destructive command without `confirm: true` | Re-run with explicit user consent |
| `COM_LOCK_TIMEOUT` | Another worker holds the STA mutex | Wait or end hung `SLDWORKS.exe` |
| `COM_RPC_FAILED` | HRESULT `0x800706BE` | Restart SolidWorks; single worker at a time |
| `COM_ROT_NOT_RUNNING` | HRESULT `0x800401E3` | Launch SolidWorks before attach |
| `SW_MATE_ERROR` | `mateError` out-param | Run `solidworks_debug_mate_entities` |

## Resources

- Generated enum dump: `generated/error-catalog.json` (`npm run docs:generate`)
- Per-code pages: add `docs/errors/<CODE>.md` as needed
- MCP: `solidworks://errors/{code}` (via `solidworks_explain_error` tool)

## Diagnostic tools

- `solidworks_diagnose_com` — ROT, mutex, interop versions
- `solidworks_diagnose_document` — active doc state
- `solidworks_diagnose_selection` — SelectionMgr snapshot
- `solidworks_explain_error` — decode code/hresult/sw_error_code
