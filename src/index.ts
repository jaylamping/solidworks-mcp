import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";

import { assertAllowedPath } from "./config.js";
import { cadConventionsCheck, designPackageValidate, designReview } from "./marengo/cad-audit.js";
import { hardwareCoverage } from "./marengo/coverage.js";
import { kinematicsConsistency, urdfExportPostcheck, urdfReadiness } from "./marengo/urdf-audit.js";
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

const marengoPathSchema = optionalPathSchema.extend({
  package_id: z.string().min(1).optional(),
  inspect_custom_properties: z.boolean().optional(),
});

async function registrySummaryHandler(registryPath?: string) {
  const resolved = registryPath ? assertAllowedPath(registryPath) : defaultRegistryPath();
  return registrySummary(resolved);
}

export async function main(): Promise<void> {
  const server = new McpServer({ name: "solidworks", version: "0.2.0" });

  const registerReadOnlyWorker = (
    name: string,
    title: string,
    description: string,
    command: Parameters<typeof runWorker>[0]["command"],
  ) => {
    server.registerTool(
      name,
      {
        title,
        description,
        inputSchema: optionalPathSchema,
        annotations: { readOnlyHint: true },
      },
      async (args: z.infer<typeof optionalPathSchema>) => {
        try {
          const filePath = args.path ? assertAllowedPath(args.path) : undefined;
          return jsonResult(await runWorker({ command, args: { path: filePath } }));
        } catch (error) {
          return errorResult(error);
        }
      },
    );
  };

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
        return jsonResult(await registrySummaryHandler(args.registry_path));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "marengo_vendor_registry_summary",
    {
      title: "Marengo vendor registry summary",
      description: "Summarize Marengo hardware/manifests/vendor-assets.json readiness.",
      inputSchema: registryPathSchema,
      annotations: { readOnlyHint: true },
    },
    async (args: z.infer<typeof registryPathSchema>) => {
      try {
        return jsonResult(await registrySummaryHandler(args.registry_path));
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

  registerReadOnlyWorker(
    "solidworks_measure",
    "Measure CAD document",
    "Return bounding box and mass-property metadata for active or specified CAD document.",
    "measure",
  );
  registerReadOnlyWorker(
    "solidworks_list_features",
    "List CAD features",
    "List top-level feature tree entries for active or specified CAD document.",
    "list_features",
  );
  registerReadOnlyWorker(
    "solidworks_inspect_document",
    "Inspect CAD document",
    "Return document type, path, saved state, units, and custom properties.",
    "inspect_document",
  );
  registerReadOnlyWorker(
    "solidworks_list_components",
    "List assembly components",
    "Return assembly component tree (name, path, quantity, suppressed, fixed).",
    "list_components",
  );
  registerReadOnlyWorker(
    "solidworks_list_reference_geometry",
    "List reference geometry",
    "List named reference planes, axes, coordinate systems, and points.",
    "list_reference_geometry",
  );
  registerReadOnlyWorker(
    "solidworks_list_bom",
    "List assembly BOM lines",
    "Return a flat component list with paths (BOM precursor).",
    "list_bom",
  );

  server.registerTool(
    "marengo_cad_conventions_check",
    {
      title: "Marengo CAD conventions check",
      description: "Validate paths and filenames under hardware/cad against cad-conventions.json.",
      inputSchema: marengoPathSchema,
      annotations: { readOnlyHint: true },
    },
    async (args: z.infer<typeof marengoPathSchema>) => {
      try {
        const filePath = args.path ? assertAllowedPath(args.path) : undefined;
        return jsonResult(
          await cadConventionsCheck({
            path: filePath,
            inspectCustomProperties: args.inspect_custom_properties,
          }),
        );
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "marengo_design_package_validate",
    {
      title: "Marengo design package validate",
      description: "Compare open assembly tree to hardware/manifests/design-packages.json.",
      inputSchema: marengoPathSchema,
      annotations: { readOnlyHint: true },
    },
    async (args: z.infer<typeof marengoPathSchema>) => {
      try {
        const filePath = args.path ? assertAllowedPath(args.path) : undefined;
        return jsonResult(await designPackageValidate({ path: filePath, packageId: args.package_id }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "marengo_design_review",
    {
      title: "Marengo design review",
      description: "Combined conventions + design package report with cad-standards checklist.",
      inputSchema: marengoPathSchema,
      annotations: { readOnlyHint: true },
    },
    async (args: z.infer<typeof marengoPathSchema>) => {
      try {
        const filePath = args.path ? assertAllowedPath(args.path) : undefined;
        return jsonResult(await designReview({ path: filePath, packageId: args.package_id }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "marengo_urdf_readiness",
    {
      title: "Marengo URDF readiness",
      description:
        "Check named URDF reference geometry against cad-conventions and kinematics.md. For assemblies, scans the full component tree (not just top-level assembly features).",
      inputSchema: optionalPathSchema,
      annotations: { readOnlyHint: true },
    },
    async (args: z.infer<typeof optionalPathSchema>) => {
      try {
        const filePath = args.path ? assertAllowedPath(args.path) : undefined;
        return jsonResult(await urdfReadiness({ path: filePath }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "marengo_kinematics_consistency",
    {
      title: "Marengo kinematics consistency",
      description: "Compare hardware/docs/kinematics.md joint names to assembly component names.",
      inputSchema: optionalPathSchema,
      annotations: { readOnlyHint: true },
    },
    async (args: z.infer<typeof optionalPathSchema>) => {
      try {
        const filePath = args.path ? assertAllowedPath(args.path) : undefined;
        return jsonResult(await kinematicsConsistency({ path: filePath }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "marengo_urdf_export_postcheck",
    {
      title: "Marengo URDF export postcheck",
      description: "After manual Brawner export, compare assets/urdf/marengo.urdf to kinematics.md and config/motors.yaml.",
      inputSchema: z.object({}),
      annotations: { readOnlyHint: true },
    },
    async () => {
      try {
        return jsonResult(await urdfExportPostcheck());
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "marengo_hardware_coverage",
    {
      title: "Marengo hardware coverage",
      description: "Compare assembly BOM lines to vendor-assets.json and hardware/bom/master-bom.csv.",
      inputSchema: optionalPathSchema,
      annotations: { readOnlyHint: true },
    },
    async (args: z.infer<typeof optionalPathSchema>) => {
      try {
        const filePath = args.path ? assertAllowedPath(args.path) : undefined;
        return jsonResult(await hardwareCoverage({ path: filePath }));
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
