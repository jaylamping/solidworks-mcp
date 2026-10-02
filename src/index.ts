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

// Sent to clients at initialize so agents learn the modeling loop without reading docs first.
const SERVER_INSTRUCTIONS = [
  "Part modeling loop: solidworks_new_document (output_path) -> solidworks_sketch (plane or face + entities) -> feature tools",
  "(solidworks_extrude / revolve / sweep / loft / shell / fillet / chamfer / hole / pattern_linear / pattern_circular / mirror / ref_plane / ref_axis)",
  "-> verify with solidworks_part_report (volume, bounding box, rebuild errors) and solidworks_render_view (PNG) -> solidworks_save_document.",
  "Always pass `path` to the .SLDPRT being edited. Lengths default to mm (`units`).",
  "Faces/edges are chosen with geometric selectors, e.g. {face:{near:[x,y,z], normal:[0,0,1]}} or {edges:{direction:[0,0,1], box:[...]}};",
  "solidworks_part_report with include_faces/include_edges lists candidates.",
  "Default sketch frames: Front Plane x->+X y->+Y (normal +Z); Top Plane x->+X y->-Z (normal +Y); Right Plane x->-Z y->+Y (normal +X).",
  "Use space:\"model\" in sketches on faces to give 3D model coordinates.",
  "Features that rebuild with errors are rolled back automatically; read the error context and retry.",
  "To review or improve an existing part: solidworks_feature_details (how it is built, dimension names) ->",
  "solidworks_simulate_static (FEA), solidworks_thickness_check, solidworks_print_check -> change it with",
  "solidworks_set_dimensions / solidworks_rollback (insert mid-tree) / solidworks_equations / new features -> re-analyze and compare.",
  "Helix sweeps: put the profile at the helix startPoint reported by solidworks_helix.",
  "See docs/part-modeling.md for the full guide.",
].join(" ");

export async function main(): Promise<void> {
  const server = new McpServer({ name: "solidworks", version: "0.4.0" }, { instructions: SERVER_INSTRUCTIONS });

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
