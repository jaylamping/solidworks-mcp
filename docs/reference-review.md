# Reference Review

Reviewed before scaffolding this repo:

- `https://github.com/eyfel/mcp-server-solidworks`
- `https://github.com/vespo92/SolidworksMCP-TS`
- `https://github.com/andrewbartels1/SolidworksMCP-python`

## Useful Patterns

- Keep SolidWorks automation behind a narrow adapter boundary.
- Expose CAD operations as small MCP tools instead of one broad mutation endpoint.
- Use structured status/error output; do not write ordinary logs to MCP stdio.
- Prefer feature-tree traversal over fragile name selection where possible.
- Treat real SolidWorks testing as separate from mock/unit testing.
- Keep design workflows checkpointed: plan, execute, inspect, iterate.

## Risks Seen Elsewhere

- Node COM modules such as `winax` can be fragile on newer Windows/build toolchains.
- Passing `null` into COM optional parameters can cause type mismatch failures; prefer omitted/undefined at the TypeScript edge and typed defaults in the worker.
- VBA macro round-trips are brittle when treated as plain text `.swp` files.
- Large tool surfaces are easy to overstate; every SolidWorks operation needs real-machine validation.

## Local Decision

Use a TypeScript MCP front door with a .NET COM worker. This keeps Cursor/MCP ergonomics while isolating Windows COM concerns, STA threading, and future SolidWorks interop assemblies in the worker.

**Invoke policy (v0.4+):** Prefer typed MCP tools. Generic `solidworks_invoke` is an allowlisted escape hatch with persist-reference identity — not in-process handles. See `docs/com-invoke-abi.md`.
