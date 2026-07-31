# COM invoke ABI

Session model for generic API access:

- **Entity identity:** SolidWorks persist references (`get_persist_reference` / `$persistRef` in invoke args). Not in-process handles — worker is single-shot per MCP call unless using `solidworks_batch_invoke`.
- **Multi-step chains:** `solidworks_batch_invoke` runs up to 50 allowlisted calls in one worker process.

## Invoke request

```json
{
  "path": "C:/allowed-root/.../assembly.SLDASM",
  "target": "app | active_doc | component | persist_ref",
  "component_name": "optional for component target",
  "persist_reference": "base64 for persist_ref target",
  "member": "MethodOrPropertyName",
  "property": false,
  "args": []
}
```

## Args encoding

| JSON | COM |
|------|-----|
| string / number / bool / null | scalar |
| `{ "$persistRef": "..." }` | resolved via `GetObjectByPersistReference3` |

## Returns

Scalars JSON-encoded. COM objects return `{ $type, $persistRef? }` when persist reference is available.

## Safety

- Read allowlist in `InvokeHandlers.cs` (`InvokeReadAllowlist`)
- Write allowlist + `SOLIDWORKS_MCP_INVOKE_WRITE=true`
- All string args that look like file paths checked against `SOLIDWORKS_MCP_ALLOWED_ROOTS` in worker

## ref/out parameters

Not supported in v1 generic invoke. Use typed MCP tools or extend allowlist with hand-wrapped methods.

## Signatures corpus

Generate on Windows: `npm run docs:signatures` → `docs/api-reference/generated/signatures.json`
