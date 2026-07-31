# SolidWorks MCP

Windows MCP server for SolidWorks automation. TypeScript handles MCP and a .NET
STA worker owns SolidWorks COM calls.

## Run

```powershell
npm install
npm run build
npm run worker:status
```

The checked-in Cursor configuration launches the local TypeScript entry point
with the extended tool tier:

```json
{
  "mcpServers": {
    "solidworks": {
      "command": "node",
      "args": ["C:/code/solidworks-mcp/node_modules/tsx/dist/cli.mjs",
               "C:/code/solidworks-mcp/src/index.ts"],
      "env": { "SOLIDWORKS_MCP_TOOL_TIER": "extended" }
    }
  }
}
```

For a compiled server, use `npm run build` followed by `npm start`.

## Path policy

Caller-supplied document paths are accepted only when they are under one of
the roots in `SOLIDWORKS_MCP_ALLOWED_ROOTS` (semicolon-separated on Windows).
The active SolidWorks document is trusted and can be used without a configured
root. Path checks are enforced by the worker as well as the MCP front door.

## Tools

The manifest contains generic document, assembly, mate, measurement, modeling,
diagnostic, invoke, and API-documentation tools. Use
`solidworks_search_tools` to discover them. URDF support is generic:
`solidworks_urdf_readiness` inspects the active document or a permitted path,
and `solidworks_add_urdf_frame` adds a coordinate-system frame to a permitted
part.

Worker failures use structured error codes. See
[docs/errors/README.md](docs/errors/README.md).

## Checks

```powershell
npm run typecheck
npm run build
npm run check:registry
npm run check:schemas
npm run validate:tools
```

`check:registry` verifies registry, worker union, manifest, scripts, and the
product-separation denylist. `validate:tools` runs API and worker smoke probes;
it reports a missing SolidWorks session or active document without requiring a
particular CAD workspace.

API reference generation and search are documented in
[docs/api-reference/README.md](docs/api-reference/README.md).
