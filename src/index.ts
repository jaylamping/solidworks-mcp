import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";

import { assertAllowedPath } from "./config.js";
import {
  defaultRegistryPath,
  registrySummary,
  stageLocalVendorAsset,
} from "./vendor-registry.js";
import { runWorker } from "./worker.js";

function jsonResult(data: unknown) {
  return {
    content: [{ type: "text" as const, text: JSON.stringify(data, null, 2) }],
  };
}

function errorResult(error: unknown) {
  const message = error instanceof Error ? error.message : String(error);
  return {
    content: [{ type: "text" as const, text: message }],
    isError: true as const,
  };
}

const optionalPathSchema = z.object({
  path: z.string().min(1).optional(),
});

const openSchema = z.object({
  path: z.string().min(1),
  start_if_missing: z.boolean().optional(),
});

const exportSchema = z.object({
  path: z.string().min(1).optional(),
  output_path: z.string().min(1),
  format: z.enum(["sldprt", "sldasm", "step", "stp", "stl", "pdf", "png"]),
  start_if_missing: z.boolean().optional(),
});

const registryPathSchema = z.object({
  registry_path: z.string().min(1).optional(),
});

const stageVendorAssetSchema = registryPathSchema.extend({
  asset_id: z.string().min(1),
  overwrite: z.boolean().optional(),
  dry_run: z.boolean().optional(),
});

export async function main(): Promise<void> {
  const server = new McpServer({ name: "solidworks", version: "0.1.0" });

  server.registerTool(
    "vendor_registry_summary",
    {
      title: "Vendor CAD registry summary",
      description: "Summarize vendor CAD assets, import status, and metadata readiness.",
      inputSchema: registryPathSchema,
      annotations: { readOnlyHint: true },
    },
    async (args: z.infer<typeof registryPathSchema>) => {
      try {
        return jsonResult(
          await registrySummary(args.registry_path ? assertAllowedPath(args.registry_path) : defaultRegistryPath()),
        );
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "vendor_stage_local_asset",
    {
      title: "Stage local vendor CAD asset",
      description:
        "Copy a local vendor CAD file into the CAD vault and record its checksum in the registry. Defaults to dry-run.",
      inputSchema: stageVendorAssetSchema,
      annotations: { readOnlyHint: false },
    },
    async (args: z.infer<typeof stageVendorAssetSchema>) => {
      try {
        return jsonResult(
          await stageLocalVendorAsset({
            registryPath: args.registry_path,
            assetId: args.asset_id,
            overwrite: args.overwrite,
            dryRun: args.dry_run ?? true,
          }),
        );
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
        return jsonResult(await runWorker({ command: "status", args }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "solidworks_open",
    {
      title: "Open CAD document",
      description: "Open a SolidWorks/STEP document from an allowed CAD root.",
      inputSchema: openSchema,
      annotations: { readOnlyHint: false },
    },
    async (args: z.infer<typeof openSchema>) => {
      try {
        const filePath = assertAllowedPath(args.path);
        return jsonResult(
          await runWorker({
            command: "open",
            args: { ...args, path: filePath },
          }),
        );
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "solidworks_export",
    {
      title: "Export CAD document",
      description: "Export active or specified document to STEP, STL, PDF, or PNG under an allowed CAD root.",
      inputSchema: exportSchema,
      annotations: { readOnlyHint: false },
    },
    async (args: z.infer<typeof exportSchema>) => {
      try {
        const filePath = args.path ? assertAllowedPath(args.path) : undefined;
        const outputPath = assertAllowedPath(args.output_path);
        return jsonResult(
          await runWorker({
            command: "export",
            args: { ...args, path: filePath, output_path: outputPath },
          }),
        );
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "solidworks_measure",
    {
      title: "Measure CAD document",
      description: "Return bounding box and mass-property metadata for active or specified CAD document.",
      inputSchema: optionalPathSchema,
      annotations: { readOnlyHint: true },
    },
    async (args: z.infer<typeof optionalPathSchema>) => {
      try {
        const filePath = args.path ? assertAllowedPath(args.path) : undefined;
        return jsonResult(await runWorker({ command: "measure", args: { path: filePath } }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "solidworks_list_features",
    {
      title: "List CAD features",
      description: "List top-level feature tree entries for active or specified CAD document.",
      inputSchema: optionalPathSchema,
      annotations: { readOnlyHint: true },
    },
    async (args: z.infer<typeof optionalPathSchema>) => {
      try {
        const filePath = args.path ? assertAllowedPath(args.path) : undefined;
        return jsonResult(await runWorker({ command: "list_features", args: { path: filePath } }));
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
