import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";

import { assertAllowedPath } from "../config.js";
import {
  alignComponentToFeature,
  assemblyToolSchemas,
  mateCoincident,
  mateDistance,
  mateParallel,
  matePerpendicular,
  saveDocument,
  torsoFrameBuildMates,
} from "../assembly-tools.js";
import { runWorker } from "../worker.js";
import {
  errorResult,
  exportSchema,
  jsonResult,
  openSchema,
  optionalPathSchema,
  setCustomPropertiesSchema,
} from "./common.js";

export function registerSolidWorksWriteTools(server: McpServer): void {
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
        return jsonResult(await runWorker({ command: "open", args: { ...args, path: filePath } }));
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
        if (!filePath) throw new Error("path is required for save_document");
        return jsonResult(await saveDocument(filePath));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "solidworks_set_custom_properties",
    {
      title: "Set custom properties",
      description:
        "Set Marengo custom properties (process, material, revision, owner) on a part or assembly and optionally save.",
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
    "solidworks_align_component_to_feature",
    {
      title: "Align component to layout feature",
      description:
        "Translate a component so a reference plane aligns to the center of a layout ICE/reference feature.",
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
      description:
        "Add a coincident mate between named references (planes, coord sys) or feature faces on two components.",
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
      description: "Add a parallel mate between references or feature faces on two components.",
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
    "solidworks_mate_distance",
    {
      title: "Distance mate",
      description:
        "Add a distance mate. Note: AddMate5 may fail silently in some SolidWorks builds — verify in the mate tree.",
      inputSchema: assemblyToolSchemas.mateRefsSchema.extend({ confirm: z.literal(true) }),
      annotations: { readOnlyHint: false },
    },
    async (args: z.infer<typeof assemblyToolSchemas.mateRefsSchema> & { confirm: true }) => {
      try {
        return jsonResult(await mateDistance(args));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "solidworks_mate_perpendicular",
    {
      title: "Perpendicular mate",
      description:
        "Add a perpendicular mate. Note: AddMate5 may fail silently — verify in the mate tree.",
      inputSchema: assemblyToolSchemas.mateRefsSchema.extend({ confirm: z.literal(true) }),
      annotations: { readOnlyHint: false },
    },
    async (args: z.infer<typeof assemblyToolSchemas.mateRefsSchema> & { confirm: true }) => {
      try {
        return jsonResult(await matePerpendicular(args));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  const setDimensionSchema = optionalPathSchema.extend({
    dimension: z.string().min(1),
    value_m: z.number(),
    configuration: z.string().min(1).optional(),
    confirm: z.literal(true),
  });

  server.registerTool(
    "solidworks_set_dimension",
    {
      title: "Set part dimension",
      description: "Set a driving dimension value (meters). Requires confirm: true.",
      inputSchema: setDimensionSchema,
      annotations: { readOnlyHint: false },
    },
    async (args: z.infer<typeof setDimensionSchema>) => {
      try {
        const filePath = args.path ? assertAllowedPath(args.path) : undefined;
        if (!filePath) throw new Error("path is required");
        return jsonResult(
          await runWorker({
            command: "set_dimension",
            args: {
              path: filePath,
              dimension: args.dimension,
              value_m: args.value_m,
              configuration: args.configuration,
            },
          }),
        );
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  const insertComponentSchema = optionalPathSchema.extend({
    part_path: z.string().min(1),
    name: z.string().min(1).optional(),
    configuration: z.string().optional(),
    save: z.boolean().optional(),
    confirm: z.literal(true),
  });

  server.registerTool(
    "solidworks_insert_component",
    {
      title: "Insert assembly component",
      description: "Insert a part into an assembly from an allowed path. Requires confirm: true.",
      inputSchema: insertComponentSchema,
      annotations: { readOnlyHint: false },
    },
    async (args: z.infer<typeof insertComponentSchema>) => {
      try {
        const filePath = args.path ? assertAllowedPath(args.path) : undefined;
        if (!filePath) throw new Error("path is required");
        const partPath = assertAllowedPath(args.part_path);
        return jsonResult(
          await runWorker({
            command: "insert_component",
            args: {
              path: filePath,
              part_path: partPath,
              name: args.name,
              configuration: args.configuration,
              save: args.save ?? true,
            },
          }),
        );
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  const setTransformSchema = optionalPathSchema.extend({
    component_name: z.string().min(1),
    transform: z.array(z.number()).length(16),
    fix: z.boolean().optional(),
    confirm: z.literal(true),
  });

  server.registerTool(
    "solidworks_set_component_transform",
    {
      title: "Set component transform",
      description: "Apply a 4x4 transform matrix to a component. Requires confirm: true.",
      inputSchema: setTransformSchema,
      annotations: { readOnlyHint: false },
    },
    async (args: z.infer<typeof setTransformSchema>) => {
      try {
        const filePath = args.path ? assertAllowedPath(args.path) : undefined;
        if (!filePath) throw new Error("path is required");
        return jsonResult(
          await runWorker({
            command: "set_component_transform",
            args: {
              path: filePath,
              component_name: args.component_name,
              transform: args.transform,
              fix: args.fix,
            },
          }),
        );
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  const rebuildSchema = optionalPathSchema.extend({
    force: z.boolean().optional(),
    confirm: z.literal(true),
  });

  server.registerTool(
    "solidworks_rebuild_document",
    {
      title: "Rebuild document",
      description: "Rebuild the active model. Requires confirm: true.",
      inputSchema: rebuildSchema,
      annotations: { readOnlyHint: false },
    },
    async (args: z.infer<typeof rebuildSchema>) => {
      try {
        const filePath = args.path ? assertAllowedPath(args.path) : undefined;
        if (!filePath) throw new Error("path is required");
        return jsonResult(
          await runWorker({
            command: "rebuild_document",
            args: { path: filePath, force: args.force ?? false },
          }),
        );
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  const addConfigSchema = optionalPathSchema.extend({
    from: z.string().min(1),
    to: z.string().min(1),
    confirm: z.literal(true),
  });

  server.registerTool(
    "solidworks_add_configuration_copy",
    {
      title: "Copy configuration",
      description: "Copy a part configuration. Requires confirm: true.",
      inputSchema: addConfigSchema,
      annotations: { readOnlyHint: false },
    },
    async (args: z.infer<typeof addConfigSchema>) => {
      try {
        const filePath = args.path ? assertAllowedPath(args.path) : undefined;
        if (!filePath) throw new Error("path is required");
        return jsonResult(
          await runWorker({
            command: "add_configuration_copy",
            args: { path: filePath, from: args.from, to: args.to },
          }),
        );
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  const persistSelectSchema = optionalPathSchema.extend({
    persist_reference: z.string().min(1),
    mark: z.number().int().optional(),
    append: z.boolean().optional(),
  });

  server.registerTool(
    "solidworks_select_by_persist_reference",
    {
      title: "Select by persist reference",
      description: "Select an entity using a base64 persist reference.",
      inputSchema: persistSelectSchema,
      annotations: { readOnlyHint: false },
    },
    async (args: z.infer<typeof persistSelectSchema>) => {
      try {
        const filePath = args.path ? assertAllowedPath(args.path) : undefined;
        if (!filePath) throw new Error("path is required");
        return jsonResult(
          await runWorker({
            command: "select_by_persist_reference",
            args: {
              path: filePath,
              persist_reference: args.persist_reference,
              mark: args.mark ?? 1,
              append: args.append ?? false,
            },
          }),
        );
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  const resolveLightweightSchema = optionalPathSchema.extend({ confirm: z.literal(true) });

  server.registerTool(
    "solidworks_resolve_lightweight",
    {
      title: "Resolve lightweight components",
      description:
        "Resolve all lightweight components in an assembly (session write). Requires confirm: true.",
      inputSchema: resolveLightweightSchema,
      annotations: { readOnlyHint: false },
    },
    async (args: z.infer<typeof resolveLightweightSchema>) => {
      try {
        const filePath = args.path ? assertAllowedPath(args.path) : undefined;
        if (!filePath) throw new Error("path is required");
        return jsonResult(
          await runWorker({ command: "resolve_lightweight", args: { path: filePath } }),
        );
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  const vendorUrdfSchema = optionalPathSchema.extend({
    save: z.boolean().optional(),
    replace_existing: z.boolean().optional(),
    confirm: z.literal(true),
  });

  server.registerTool(
    "marengo_vendor_add_rs03_urdf_frame",
    {
      title: "Add RS03 URDF frame",
      description: "Add vendor RS03 URDF reference frame to a part. Requires confirm: true.",
      inputSchema: vendorUrdfSchema,
      annotations: { readOnlyHint: false },
    },
    async (args: z.infer<typeof vendorUrdfSchema>) => {
      try {
        const filePath = args.path ? assertAllowedPath(args.path) : undefined;
        if (!filePath) throw new Error("path is required");
        return jsonResult(
          await runWorker({
            command: "vendor_add_rs03_urdf_frame",
            args: {
              path: filePath,
              save: args.save ?? true,
              replace_existing: args.replace_existing ?? true,
            },
          }),
        );
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "marengo_torso_frame_build",
    {
      title: "Build Marengo torso frame mates",
      description:
        "DESTRUCTIVE — only when the user explicitly asked. Requires confirm: true. Aligns 12×2020 + 16× brackets to layout ICE and saves the frame assembly.",
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
}
