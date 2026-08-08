import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { z } from "zod";

import { registerApiCatalogResources } from "./api-catalog.js";
import { readRecentAuditEntries } from "./audit-log.js";
import { packageRoot } from "./config.js";
import { registerSolidWorksTools } from "./tool-registry.js";
import { registerDocsTools } from "./tools/docs-search.js";
import { errorResult, jsonResult } from "./tools/common.js";
import { registerSolidWorksInvokeTools } from "./tools/solidworks-invoke.js";
import { registerUrdfTools } from "./tools/urdf.js";
import { runWorker } from "./worker.js";

function mcpBuildInfo(): { mcpVersion: string; buildId: string } {
  const pkgPath = path.join(packageRoot(), "package.json");
  const pkg = JSON.parse(readFileSync(pkgPath, "utf8")) as { version?: string };
  const buildId = path.basename(fileURLToPath(import.meta.url));
  return { mcpVersion: pkg.version ?? "0.0.0", buildId };
}

export async function main(): Promise<void> {
  const server = new McpServer({ name: "solidworks", version: "0.4.0" });

  registerSolidWorksTools(server);
  registerUrdfTools(server);
  registerApiCatalogResources(server);
  registerDocsTools(server);
  registerSolidWorksInvokeTools(server);

  server.registerTool(
    "solidworks_audit_log_recent",
    {
      title: "Recent MCP audit log",
      description: "Read recent SolidWorks write-tool audit entries.",
      inputSchema: z.object({ limit: z.number().int().min(1).max(100).optional() }),
      annotations: { readOnlyHint: true },
    },
    async (args: { limit?: number }) => {
      try {
        return jsonResult({ entries: readRecentAuditEntries(args.limit ?? 20) });
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "solidworks_status",
    {
      title: "SolidWorks status",
      description: "Attach to SolidWorks if running and report version/active document metadata.",
      inputSchema: z.object({ start_if_missing: z.boolean().optional() }),
      annotations: { readOnlyHint: true },
    },
    async (args) => {
      try {
        const data = await runWorker({ command: "status", args });
        const docVersion = (data as { version?: string })?.version;
        const apiVersionPath = path.join(packageRoot(), "docs/api-reference/VERSION.md");
        let docVersionWarning: string | undefined;
        try {
          const versionMd = readFileSync(apiVersionPath, "utf8");
          const targetYear = versionMd.match(/Target year:\*\*\s*(\d+)/)?.[1];
          if (targetYear && docVersion && !docVersion.includes(targetYear)) {
            docVersionWarning = `Installed SolidWorks (${docVersion}) may differ from API docs year (${targetYear}).`;
          }
        } catch {
          /* no VERSION.md */
        }
        return jsonResult({
          ...(data as Record<string, unknown>),
          ...mcpBuildInfo(),
          docVersionWarning,
        });
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  await server.connect(new StdioServerTransport());
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
