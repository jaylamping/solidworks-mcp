import { readFileSync } from "node:fs";
import path from "node:path";

import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";

import { assertAllowedPath, packageRoot } from "./config.js";
import { formatErrorForMcp } from "./errors.js";
import { cadConventionsCheck, designPackageValidate, designReview } from "./marengo/cad-audit.js";
import { hardwareCoverage } from "./marengo/coverage.js";
import {
  actuatorEnvelopeCheck,
  bilateralSymmetryCheck,
  configVariantDiff,
  exportBomCsv,
  jointAxisExtract,
  jointLimitValidate,
  linkMassProperties,
  referenceGeometryAudit,
  subassemblySyncCheck,
  urdfExportPrepare,
} from "./marengo/humanoid-audit.js";
import { kinematicsConsistency, urdfExportPostcheck, urdfReadiness } from "./marengo/urdf-audit.js";
import { shoulderBracketProbe } from "./marengo/shoulder-bracket-probe.js";
import {
  runBilateralMirrorWorkflow,
  runBracketRepairWorkflow,
  runComputeShelfMateWorkflow,
  runShoulderRollSetupWorkflow,
  runTorsoAsmBuildWorkflow,
} from "./marengo/workflows.js";
import { registrySummary, stageLocalVendorAsset, defaultRegistryPath } from "./vendor-registry.js";
import {
  alignComponentToFeature,
  assemblyToolSchemas,
  getFeatureBox,
  mateCoincident,
  mateParallel,
  probeFeatureFaces,
  saveDocument,
  torsoFrameBuildMates,
} from "./assembly-tools.js";
import { optionalPathSchema } from "./schemas/document.js";
import { readRecentAuditEntries } from "./audit-log.js";
import { runWorker } from "./worker.js";

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

const setCustomPropertiesSchema = z.object({
  path: z.string().min(1),
  properties: z.record(z.string(), z.string()),
  save: z.boolean().optional(),
});

function mcpBuildInfo(): { mcpVersion: string; buildId: string } {
  const pkgPath = path.join(packageRoot(), "package.json");
  const pkg = JSON.parse(readFileSync(pkgPath, "utf8")) as { version?: string };
  return { mcpVersion: pkg.version ?? "0.0.0", buildId: "marengo-tools" };
}

export function registerMarengoTools(server: McpServer): void {
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
        const resolved = args.registry_path
          ? assertAllowedPath(args.registry_path)
          : defaultRegistryPath();
        return jsonResult(await registrySummary(resolved));
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
        const resolved = args.registry_path
          ? assertAllowedPath(args.registry_path)
          : defaultRegistryPath();
        return jsonResult(await registrySummary(resolved));
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
        "Copy a local vendor CAD file into the CAD vault and record its checksum in the registry.",
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
        const data = await runWorker({ command: "status", args });
        return jsonResult({ ...(data as Record<string, unknown>), ...mcpBuildInfo() });
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "solidworks_set_custom_properties",
    {
      title: "Set custom properties",
      description: "Set Marengo custom properties on a part or assembly and optionally save.",
      inputSchema: setCustomPropertiesSchema,
      annotations: { readOnlyHint: false },
    },
    async (args: z.infer<typeof setCustomPropertiesSchema>) => {
      try {
        const filePath = assertAllowedPath(args.path);
        return jsonResult(
          await runWorker({
            command: "set_custom_properties",
            args: { path: filePath, properties: args.properties, save: args.save ?? true },
          }),
        );
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "solidworks_probe_feature_faces",
    {
      title: "Probe feature faces",
      description: "List face count/area/planarity for a component feature.",
      inputSchema: assemblyToolSchemas.featureProbeSchema,
      annotations: { readOnlyHint: true },
    },
    async (args: z.infer<typeof assemblyToolSchemas.featureProbeSchema>) => {
      try {
        return jsonResult(await probeFeatureFaces(args));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "solidworks_get_feature_box",
    {
      title: "Feature bounding box",
      description: "Return assembly-space bounding box for a named feature on a component.",
      inputSchema: assemblyToolSchemas.featureProbeSchema,
      annotations: { readOnlyHint: true },
    },
    async (args: z.infer<typeof assemblyToolSchemas.featureProbeSchema>) => {
      try {
        return jsonResult(await getFeatureBox(args));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "solidworks_save_document",
    {
      title: "Save CAD document",
      description: "Save active or specified SolidWorks document.",
      inputSchema: optionalPathSchema,
      annotations: { readOnlyHint: false },
    },
    async (args: z.infer<typeof optionalPathSchema>) => {
      try {
        const filePath = args.path ? assertAllowedPath(args.path) : undefined;
        if (!filePath) {
          throw new Error("path is required for save_document");
        }
        return jsonResult(await saveDocument(filePath));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "solidworks_align_component_to_feature",
    {
      title: "Align component to layout feature",
      description: "Translate a component so a reference plane aligns to a layout ICE feature.",
      inputSchema: assemblyToolSchemas.alignSchema,
      annotations: { readOnlyHint: false },
    },
    async (args: z.infer<typeof assemblyToolSchemas.alignSchema>) => {
      try {
        return jsonResult(await alignComponentToFeature(args));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "solidworks_mate_coincident",
    {
      title: "Coincident mate",
      description: "Add a coincident mate between named references on two components.",
      inputSchema: assemblyToolSchemas.mateRefsSchema,
      annotations: { readOnlyHint: false },
    },
    async (args: z.infer<typeof assemblyToolSchemas.mateRefsSchema>) => {
      try {
        return jsonResult(await mateCoincident(args));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "solidworks_mate_parallel",
    {
      title: "Parallel mate",
      description: "Add a parallel mate between references on two components.",
      inputSchema: assemblyToolSchemas.mateRefsSchema,
      annotations: { readOnlyHint: false },
    },
    async (args: z.infer<typeof assemblyToolSchemas.mateRefsSchema>) => {
      try {
        return jsonResult(await mateParallel(args));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "marengo_torso_frame_build",
    {
      title: "Build Marengo torso frame mates",
      description: "DESTRUCTIVE — requires confirm: true. Aligns frame to layout ICE.",
      inputSchema: assemblyToolSchemas.torsoFrameBuildSchema,
      annotations: { readOnlyHint: false },
    },
    async (args: z.infer<typeof assemblyToolSchemas.torsoFrameBuildSchema>) => {
      try {
        return jsonResult(await torsoFrameBuildMates(args));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  const registerMarengoAudit = (
    name: string,
    title: string,
    description: string,
    handler: (args: z.infer<typeof marengoPathSchema>) => Promise<unknown>,
  ) => {
    server.registerTool(
      name,
      { title, description, inputSchema: marengoPathSchema, annotations: { readOnlyHint: true } },
      async (args: z.infer<typeof marengoPathSchema>) => {
        try {
          const filePath = args.path ? assertAllowedPath(args.path) : undefined;
          return jsonResult(await handler({ ...args, path: filePath }));
        } catch (error) {
          return errorResult(error);
        }
      },
    );
  };

  registerMarengoAudit(
    "marengo_cad_conventions_check",
    "Marengo CAD conventions check",
    "Validate paths and filenames under hardware/cad against cad-conventions.json.",
    async (args) =>
      cadConventionsCheck({
        path: args.path,
        inspectCustomProperties: args.inspect_custom_properties,
      }),
  );

  registerMarengoAudit(
    "marengo_design_package_validate",
    "Marengo design package validate",
    "Compare open assembly tree to hardware/manifests/design-packages.json.",
    async (args) => designPackageValidate({ path: args.path, packageId: args.package_id }),
  );

  registerMarengoAudit(
    "marengo_design_review",
    "Marengo design review",
    "Combined conventions + design package report.",
    async (args) => designReview({ path: args.path, packageId: args.package_id }),
  );

  server.registerTool(
    "marengo_urdf_readiness",
    {
      title: "Marengo URDF readiness",
      description: "Check named URDF reference geometry against cad-conventions and kinematics.md.",
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
      description: "Compare kinematics.md joint names to assembly component names.",
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
      description: "Compare exported URDF to kinematics.md and config/motors.yaml.",
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
      description: "Compare assembly BOM to vendor-assets.json and master-bom.csv.",
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
    "marengo_link_mass_properties",
    {
      title: "Marengo link mass properties",
      description: "Per-link mass/CG/inertia aggregated from assembly component tree.",
      inputSchema: optionalPathSchema,
      annotations: { readOnlyHint: true },
    },
    async (args: z.infer<typeof optionalPathSchema>) => {
      try {
        const filePath = args.path ? assertAllowedPath(args.path) : undefined;
        return jsonResult(await linkMassProperties({ path: filePath }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "marengo_joint_axis_extract",
    {
      title: "Marengo joint axis extract",
      description: "Read URDF coord-sys / mate axis direction per joint name.",
      inputSchema: optionalPathSchema,
      annotations: { readOnlyHint: true },
    },
    async (args: z.infer<typeof optionalPathSchema>) => {
      try {
        const filePath = args.path ? assertAllowedPath(args.path) : undefined;
        return jsonResult(await jointAxisExtract({ path: filePath }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "marengo_bilateral_symmetry_check",
    {
      title: "Marengo bilateral symmetry check",
      description: "Compare L/R component transforms within tolerance.",
      inputSchema: optionalPathSchema,
      annotations: { readOnlyHint: true },
    },
    async (args: z.infer<typeof optionalPathSchema>) => {
      try {
        const filePath = args.path ? assertAllowedPath(args.path) : undefined;
        return jsonResult(await bilateralSymmetryCheck({ path: filePath }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "marengo_actuator_envelope_check",
    {
      title: "Marengo actuator envelope check",
      description: "Motor bbox vs cavity feature interference check.",
      inputSchema: optionalPathSchema,
      annotations: { readOnlyHint: true },
    },
    async (args: z.infer<typeof optionalPathSchema>) => {
      try {
        const filePath = args.path ? assertAllowedPath(args.path) : undefined;
        return jsonResult(await actuatorEnvelopeCheck({ path: filePath }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "marengo_config_variant_diff",
    {
      title: "Marengo config variant diff",
      description: "Compare dimensions/configs across arm/torso variants.",
      inputSchema: optionalPathSchema,
      annotations: { readOnlyHint: true },
    },
    async (args: z.infer<typeof optionalPathSchema>) => {
      try {
        const filePath = args.path ? assertAllowedPath(args.path) : undefined;
        return jsonResult(await configVariantDiff({ path: filePath }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  const workflowSchema = optionalPathSchema.extend({ confirm: z.literal(true).optional() });

  server.registerTool(
    "marengo_workflow_torso_asm_build",
    {
      title: "Marengo torso assembly build workflow",
      description: "Composite workflow: frame mates + shoulder mounts. Requires confirm for writes.",
      inputSchema: workflowSchema,
      annotations: { readOnlyHint: false },
    },
    async (args: z.infer<typeof workflowSchema>) => {
      try {
        return jsonResult(await runTorsoAsmBuildWorkflow(args));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "marengo_workflow_shoulder_roll_setup",
    {
      title: "Marengo shoulder roll setup workflow",
      description: "Composite workflow for shoulder roll motor placement and mating.",
      inputSchema: workflowSchema,
      annotations: { readOnlyHint: false },
    },
    async (args: z.infer<typeof workflowSchema>) => {
      try {
        return jsonResult(await runShoulderRollSetupWorkflow(args));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "marengo_workflow_compute_shelf_mate",
    {
      title: "Marengo compute shelf mate workflow",
      description: "Build compute shelf part and mate into torso assembly.",
      inputSchema: workflowSchema,
      annotations: { readOnlyHint: false },
    },
    async (args: z.infer<typeof workflowSchema>) => {
      try {
        return jsonResult(await runComputeShelfMateWorkflow(args));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "marengo_workflow_bilateral_mirror",
    {
      title: "Marengo bilateral mirror workflow",
      description: "Mirror shoulder bracket and swap L/R pitch instances.",
      inputSchema: workflowSchema,
      annotations: { readOnlyHint: false },
    },
    async (args: z.infer<typeof workflowSchema>) => {
      try {
        return jsonResult(await runBilateralMirrorWorkflow(args));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "marengo_workflow_bracket_repair",
    {
      title: "Marengo bracket repair workflow",
      description: "Checkpoint, diagnose part save, and save assembly. Requires confirm: true.",
      inputSchema: workflowSchema.extend({ confirm: z.literal(true) }),
      annotations: { readOnlyHint: false },
    },
    async (args: z.infer<typeof workflowSchema> & { confirm: true }) => {
      try {
        return jsonResult(await runBracketRepairWorkflow(args));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  const humanoidTools: Array<{
    name: string;
    title: string;
    description: string;
    handler: (path?: string) => Promise<unknown>;
  }> = [
    {
      name: "marengo_reference_geometry_audit",
      title: "Marengo reference geometry audit",
      description: "Deep scan for urdf_* reference geometry on assembly/part.",
      handler: (p) => referenceGeometryAudit({ path: p }),
    },
    {
      name: "marengo_joint_limit_validate",
      title: "Marengo joint limit validate",
      description: "List limit/angle mates and cross-check against kinematics.md.",
      handler: (p) => jointLimitValidate({ path: p }),
    },
    {
      name: "marengo_subassembly_sync_check",
      title: "Marengo subassembly sync check",
      description: "Frame-in-torso consistency and broken external references.",
      handler: (p) => subassemblySyncCheck({ path: p }),
    },
    {
      name: "marengo_export_bom_csv",
      title: "Marengo export BOM CSV",
      description: "Flat BOM from SolidWorks as CSV for master-bom review.",
      handler: (p) => exportBomCsv({ path: p }),
    },
    {
      name: "marengo_urdf_export_prepare",
      title: "Marengo URDF export prepare",
      description: "Pre-flight checklist before manual Brawner URDF export.",
      handler: (p) => urdfExportPrepare({ path: p }),
    },
  ];

  for (const tool of humanoidTools) {
    server.registerTool(
      tool.name,
      {
        title: tool.title,
        description: tool.description,
        inputSchema: optionalPathSchema,
        annotations: { readOnlyHint: true },
      },
      async (args: z.infer<typeof optionalPathSchema>) => {
        try {
          const filePath = args.path ? assertAllowedPath(args.path) : undefined;
          return jsonResult(await tool.handler(filePath));
        } catch (error) {
          return errorResult(error);
        }
      },
    );
  }

  server.registerTool(
    "solidworks_audit_log_recent",
    {
      title: "Recent MCP audit log",
      description: "Read recent write-tool audit entries from hardware/cad/.mcp-audit.jsonl.",
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

  const actuatorModelSchema = z.enum(["rs00", "rs02", "rs03", "rs04", "rs05"]).optional();
  const actuatorReadSchema = z.object({
    model: actuatorModelSchema,
    vendor_path: z.string().optional(),
    plane_name: z.string().optional(),
  });
  const actuatorMountSchema = optionalPathSchema.extend({
    model: actuatorModelSchema,
    plane_name: z.string().optional(),
    bolt_circle_diameter_mm: z.number().optional(),
    bolt_count: z.number().int().min(1).optional(),
    hole_diameter_mm: z.number().optional(),
    start_angle_deg: z.number().optional(),
    save: z.boolean().optional(),
    confirm: z.literal(true),
  });
  const actuatorAssemblySchema = z.object({
    path: z.string().min(1).optional(),
    model: actuatorModelSchema,
    bracket_part_path: z.string().optional(),
    bracket_component: z.string().optional(),
    tool_component: z.string().optional(),
    use_selection: z.boolean().optional(),
    clearance_mm: z.number().optional(),
    vendor_path: z.string().optional(),
    component_prefix: z.string().optional(),
    save: z.boolean().optional(),
    confirm: z.literal(true),
  });

  server.registerTool(
    "marengo_actuator_list_models",
    {
      title: "Marengo actuator catalog",
      description: "List Robstride RS00/RS02/RS03/RS04/RS05 vendor paths, envelopes, and default mount hole patterns.",
      inputSchema: z.object({}),
      annotations: { readOnlyHint: true },
    },
    async () => {
      try {
        return jsonResult(await runWorker({ command: "actuator_list_models", args: {} }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "marengo_actuator_get_envelope",
    {
      title: "Marengo actuator envelope",
      description: "Measure vendor part bounding box and compare to catalog defaults for RS02/RS03/RS04.",
      inputSchema: actuatorReadSchema,
      annotations: { readOnlyHint: true },
    },
    async (args: z.infer<typeof actuatorReadSchema>) => {
      try {
        return jsonResult(await runWorker({ command: "actuator_get_envelope", args }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "marengo_actuator_mount_holes",
    {
      title: "Marengo actuator mount holes",
      description:
        "Cut a circular bolt-hole pattern on a bracket mount face using RS02/RS03/RS04 catalog defaults (sketch + through-cut).",
      inputSchema: actuatorMountSchema,
      annotations: { destructiveHint: true },
    },
    async (args: z.infer<typeof actuatorMountSchema>) => {
      try {
        const filePath = args.path ? assertAllowedPath(args.path) : undefined;
        return jsonResult(
          await runWorker({
            command: "actuator_mount_hole_pattern",
            args: { ...args, path: filePath, confirm: true },
          }),
        );
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "marengo_actuator_cut_cavity",
    {
      title: "Marengo actuator cavity cut",
      description:
        "Indent/combine cavity cut for a Robstride actuator into a bracket. Pass component names or highlight bracket (+ optional actuator) and set use_selection: true.",
      inputSchema: actuatorAssemblySchema,
      annotations: { destructiveHint: true },
    },
    async (args: z.infer<typeof actuatorAssemblySchema>) => {
      try {
        const filePath = args.path ? assertAllowedPath(args.path) : undefined;
        return jsonResult(
          await runWorker({
            command: "actuator_cut_cavity",
            args: { ...args, path: filePath, confirm: true },
          }),
        );
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  const shoulderProbeSchema = optionalPathSchema.extend({
    side: z.enum(["left", "right"]).optional(),
    model: actuatorModelSchema.optional(),
  });

  server.registerTool(
    "marengo_shoulder_bracket_probe",
    {
      title: "Marengo shoulder bracket probe",
      description:
        "Read-only bundle of RS03 envelope, shoulder_mount refs, motor boxes/transforms, and suggested starter bracket dims for iterative shoulder pitch design.",
      inputSchema: shoulderProbeSchema,
      annotations: { readOnlyHint: true },
    },
    async (args: z.infer<typeof shoulderProbeSchema>) => {
      try {
        const filePath = args.path ? assertAllowedPath(args.path) : undefined;
        return jsonResult(
          await shoulderBracketProbe({
            path: filePath,
            side: args.side,
            model: args.model,
          }),
        );
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  const resolveSelectionSchema = optionalPathSchema;

  server.registerTool(
    "marengo_resolve_selection",
    {
      title: "Marengo resolve SolidWorks selection",
      description:
        "Resolve what is currently highlighted in SolidWorks — the referent for 'this', 'here', or 'the selected body/plane'. Returns labels, toolArgs, and commands that accept use_selection.",
      inputSchema: resolveSelectionSchema,
      annotations: { readOnlyHint: true },
    },
    async (args: z.infer<typeof resolveSelectionSchema>) => {
      try {
        const filePath = args.path ? assertAllowedPath(args.path) : undefined;
        return jsonResult(
          await runWorker({
            command: "resolve_selection",
            args: { ...args, path: filePath },
          }),
        );
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "marengo_actuator_place_vendor",
    {
      title: "Marengo place actuator vendor part",
      description: "Insert RS02/RS03/RS04 vendor SLDPRT into an assembly with a predictable component prefix.",
      inputSchema: actuatorAssemblySchema,
      annotations: { destructiveHint: true },
    },
    async (args: z.infer<typeof actuatorAssemblySchema>) => {
      try {
        const filePath = args.path ? assertAllowedPath(args.path) : undefined;
        return jsonResult(
          await runWorker({
            command: "actuator_insert_vendor",
            args: { ...args, path: filePath, confirm: true },
          }),
        );
      } catch (error) {
        return errorResult(error);
      }
    },
  );
}
