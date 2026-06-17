import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";

import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

import { packageRoot } from "./config.js";
import { registerDocsTools } from "./tools/docs-search.js";
import { registerMarengoAuditTools } from "./tools/marengo-audit.js";
import { errorResult, jsonResult } from "./tools/common.js";
import { registerSolidWorksInvokeTools } from "./tools/solidworks-invoke.js";
import { registerSolidWorksReadTools } from "./tools/solidworks-read.js";
import { registerSolidWorksWriteTools } from "./tools/solidworks-write.js";
import { runWorker } from "./worker.js";

function mcpBuildInfo(): { mcpVersion: string; buildId: string } {
  const pkgPath = path.join(packageRoot(), "package.json");
  const pkg = JSON.parse(readFileSync(pkgPath, "utf8")) as { version?: string };
  const buildId = path.basename(fileURLToPath(import.meta.url));
  return { mcpVersion: pkg.version ?? "0.0.0", buildId };
}

export async function main(): Promise<void> {
  const server = new McpServer({ name: "solidworks", version: "0.4.0" });

  registerDocsTools(server);
  registerSolidWorksReadTools(server);
  registerSolidWorksWriteTools(server);
  registerMarengoAuditTools(server);
  registerSolidWorksInvokeTools(server);

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
