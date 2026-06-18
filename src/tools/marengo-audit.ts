import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";

import { assertAllowedPath } from "../config.js";
import { cadConventionsCheck, designPackageValidate, designReview } from "../marengo/cad-audit.js";
import { hardwareCoverage } from "../marengo/coverage.js";
import { kinematicsConsistency, urdfExportPostcheck, urdfReadiness } from "../marengo/urdf-audit.js";
import {
  defaultRegistryPath,
  registrySummary,
  stageLocalVendorAsset,
} from "../vendor-registry.js";
import { runWorker } from "../worker.js";
import {
  errorResult,
  jsonResult,
  marengoPathSchema,
  optionalPathSchema,
  registryPathSchema,
  stageVendorAssetSchema,
} from "./common.js";

async function registrySummaryHandler(registryPath?: string) {
  const resolved = registryPath ? assertAllowedPath(registryPath) : defaultRegistryPath();
  return registrySummary(resolved);
}

export function registerMarengoAuditTools(server: McpServer): void {
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
      description: "Summarize Marengo cad/manifests/vendor-assets.json readiness.",
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
    "marengo_cad_conventions_check",
    {
      title: "Marengo CAD conventions check",
      description: "Validate paths and filenames under cad against cad-conventions.json.",
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
      description: "Compare open assembly tree to cad/manifests/design-packages.json.",
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
      description:
        "After manual Brawner export, compare assets/urdf/marengo.urdf to kinematics.md and config/motors.yaml.",
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

  server.registerTool(
    "marengo_clearance_summary",
    {
      title: "Marengo clearance summary",
      description:
        "Assembly measure + interference count + URDF readiness gaps (static pose).",
      inputSchema: optionalPathSchema,
      annotations: { readOnlyHint: true },
    },
    async (args: z.infer<typeof optionalPathSchema>) => {
      try {
        const filePath = args.path ? assertAllowedPath(args.path) : undefined;
        if (!filePath) throw new Error("path is required");

        const [measure, interferences, urdf] = await Promise.all([
          runWorker({ command: "measure", args: { path: filePath } }),
          runWorker({ command: "list_interferences", args: { path: filePath } }),
          urdfReadiness({ path: filePath }),
        ]);

        return jsonResult({
          path: filePath,
          measure,
          interferences,
          urdfReadiness: urdf,
          note: "Static assembly pose only; joint-range swept clearance not included.",
        });
      } catch (error) {
        return errorResult(error);
      }
    },
  );
}
