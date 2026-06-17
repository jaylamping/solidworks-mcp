import { readFileSync, existsSync } from "node:fs";
import path from "node:path";

import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";

import { packageRoot } from "./config.js";
import { formatErrorForMcp } from "./errors.js";

function jsonResult(data: unknown) {
  return {
    content: [{ type: "text" as const, text: JSON.stringify(data, null, 2) }],
  };
}

function errorResult(error: unknown) {
  return {
    content: [{ type: "text" as const, text: formatErrorForMcp(error) }],
    isError: true as const,
  };
}

function catalogPath(): string {
  return path.join(packageRoot(), "generated/api-catalog.json");
}

function allowlistPath(): string {
  return path.join(packageRoot(), "generated/invoke-allowlist.json");
}

export function registerApiCatalogResources(server: McpServer): void {
  server.registerResource(
    "solidworks-api-catalog",
    "solidworks://api-catalog",
    {
      title: "SolidWorks API catalog",
      description: "Generated interop reflection catalog (interfaces and methods).",
      mimeType: "application/json",
    },
    async () => {
      const file = catalogPath();
      const text = existsSync(file)
        ? readFileSync(file, "utf8")
        : JSON.stringify({ stub: true, message: "Run npm run docs:generate to build api-catalog.json" });
      return { contents: [{ uri: "solidworks://api-catalog", mimeType: "application/json", text }] };
    },
  );

  server.registerTool(
    "solidworks_search_api",
    {
      title: "Search SolidWorks API catalog",
      description: "Search generated API catalog by interface or method name.",
      inputSchema: z.object({ query: z.string().min(1), limit: z.number().int().optional() }),
      annotations: { readOnlyHint: true },
    },
    async (args: { query: string; limit?: number }) => {
      try {
        const file = catalogPath();
        if (!existsSync(file)) {
          return jsonResult({ query: args.query, matches: [], stub: true });
        }
        const catalog = JSON.parse(readFileSync(file, "utf8")) as {
          interfaces?: Array<{ name: string; methods?: string[] }>;
        };
        const q = args.query.toLowerCase();
        const limit = args.limit ?? 25;
        const matches = (catalog.interfaces ?? [])
          .flatMap((iface) =>
            (iface.methods ?? []).map((method) => ({ interface: iface.name, method })),
          )
          .filter((row) => row.interface.toLowerCase().includes(q) || row.method.toLowerCase().includes(q))
          .slice(0, limit);
        return jsonResult({ query: args.query, count: matches.length, matches });
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "solidworks_invoke",
    {
      title: "Invoke allowlisted SolidWorks COM method",
      description: "Low-level escape hatch for advanced COM calls on the allowlist.",
      inputSchema: z.object({
        interface: z.string().min(1),
        method: z.string().min(1),
        args: z.array(z.unknown()).optional(),
        confirm: z.literal(true),
      }),
      annotations: { readOnlyHint: false },
    },
    async (args: { interface: string; method: string; args?: unknown[]; confirm: true }) => {
      try {
        const allowlist = existsSync(allowlistPath())
          ? (JSON.parse(readFileSync(allowlistPath(), "utf8")) as { allowed?: string[] }).allowed ?? []
          : [];
        const key = `${args.interface}.${args.method}`;
        if (!allowlist.includes(key)) {
          throw new Error(`COM invoke not on allowlist: ${key}`);
        }
        return jsonResult({
          stub: true,
          key,
          message: "solidworks_invoke dispatches via worker in a future release.",
          requestedArgs: args.args ?? [],
        });
      } catch (error) {
        return errorResult(error);
      }
    },
  );
}
