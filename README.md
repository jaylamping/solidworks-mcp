# SolidWorks MCP

[Model Context Protocol](https://modelcontextprotocol.io/) server that lets AI agents drive **SolidWorks** on Windows — open documents, inspect assemblies, create mates, measure geometry, export files, and call allowlisted SolidWorks API members.

TypeScript speaks MCP. A .NET STA worker owns SolidWorks COM so the CAD session stays stable under tool calls.

> **Requirements:** Windows · SolidWorks installed and licensed · Node.js 20+ · .NET 8 SDK  
> SolidWorks must be running (or launchable) in the same desktop session as the MCP server.

## Why this exists

SolidWorks automation is powerful but awkward for agents: COM is STA, selection state is fragile, and one bad parallel call can take down the app. This server wraps the useful workflows as typed MCP tools, serializes COM access, and returns structured errors instead of opaque HRESULTs.

## Quick start

```powershell
git clone https://github.com/jaylamping/solidworks-mcp.git
cd solidworks-mcp
npm install
npm run build
npm run worker:status
```

`worker:status` should report a live SolidWorks COM connection. If it does not, launch SolidWorks once from the Start Menu, then retry.

### Cursor (or any MCP client)

Add a server entry that points at your clone. Example for Cursor (`.cursor/mcp.json` or global MCP settings):

```json
{
  "mcpServers": {
    "solidworks": {
      "command": "node",
      "args": [
        "C:/path/to/solidworks-mcp/node_modules/tsx/dist/cli.mjs",
        "C:/path/to/solidworks-mcp/src/index.ts"
      ],
      "env": {
        "SOLIDWORKS_MCP_TOOL_TIER": "extended",
        "SOLIDWORKS_MCP_ALLOWED_ROOTS": "C:/cad;D:/projects"
      }
    }
  }
}
```

For a compiled server after `npm run build`, use `node` with `dist/index.js` (same as `npm start`).

**Important:** do not run SolidWorks MCP tools in parallel against the same session. COM is single-threaded; the server serializes workers, but agents should still call tools one at a time.

## What you can do

Roughly 100+ tools across:

| Area | Examples |
|------|----------|
| Documents | open, save, close, rebuild, pack-and-go, export |
| Assemblies | list components, transforms, interference, BOM, DOF |
| Mates | coincident, distance, parallel, perpendicular, width, limit angle, suppress/delete |
| Modeling | sketches, extrude/cut, fillet/chamfer, patterns, mirror |
| Measure / inspect | mass properties, measure, feature/body boxes, persist refs |
| Selection | resolve current selection, select by persist reference or ray |
| URDF helpers | readiness check, add coordinate-system frames |
| Escape hatches | `solidworks_invoke` / `solidworks_batch_invoke`, API doc search |

Discover tools at runtime with `solidworks_search_tools`. Per-tool pages under [`docs/api/`](docs/api/) are generated from the tool manifest and Zod schemas (summary, flags, and parameter tables). Do not edit those markdown files by hand; run `npm run docs:generate` and keep them in sync with `npm run docs:check`.

Tool exposure is gated by `SOLIDWORKS_MCP_TOOL_TIER`: `core` · `extended` · `advanced` · `debug` · `all` (default).

## Path policy (safety)

Caller-supplied file paths are accepted only under roots listed in:

```text
SOLIDWORKS_MCP_ALLOWED_ROOTS=C:\cad;D:\work
```

(semicolon-separated on Windows). The **active** SolidWorks document is trusted and does not require a configured root. Path checks run in both the MCP front door and the .NET worker.

Destructive operations often require an explicit `confirm: true` argument. Low-level `solidworks_invoke` writes need `SOLIDWORKS_MCP_INVOKE_WRITE=true`.

## Architecture

```text
MCP client ──stdio──► Node (TypeScript) ──queue──► SolidWorksComWorker (.NET 8)
                                                      │
                                                      ▼
                                              SolidWorks COM (STA)
```

- **Node** — MCP protocol, Zod schemas, tool registry, path guards, worker queue  
- **Worker** — attaches to `SldWorks.Application`, runs one command per process (optional persistent mode via `SOLIDWORKS_MCP_PERSISTENT_WORKER=1`)  
- **Interop** — references SolidWorks redistributable DLLs from a local SolidWorks install (`api\redist\`)

## Environment variables

| Variable | Purpose |
|----------|---------|
| `SOLIDWORKS_MCP_ALLOWED_ROOTS` | Semicolon-separated directories allowed for open/save/export paths |
| `SOLIDWORKS_MCP_TOOL_TIER` | `core` / `extended` / `advanced` / `debug` / `all` |
| `SOLIDWORKS_MCP_INVOKE_WRITE` | Allow write members through `solidworks_invoke` |
| `SOLIDWORKS_MCP_PERSISTENT_WORKER` | `1` to keep a long-lived worker process |

## Development checks

```powershell
npm run typecheck
npm run build
npm run check:registry
npm run check:schemas
npm run validate:tools
```

`validate:tools` smoke-probes the API and worker; it reports a missing SolidWorks session without requiring a particular CAD workspace.

More detail:

- [Troubleshooting](docs/troubleshooting.md)
- [Error catalog](docs/errors/README.md)
- [CAD automation notes](docs/cad-automation.md)
- [COM invoke ABI](docs/com-invoke-abi.md)
- [API reference corpus](docs/api-reference/README.md) (generated / scraped locally)

## License

MIT — see [LICENSE](LICENSE).

SolidWorks® is a trademark of Dassault Systèmes. This project is not affiliated with or endorsed by Dassault Systèmes.
