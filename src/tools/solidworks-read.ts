import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";

import { assertAllowedPath } from "../config.js";
import {
  assemblyToolSchemas,
  getFeatureBox,
  listMates,
  probeFeatureFaces,
} from "../assembly-tools.js";
import { runWorker } from "../worker.js";
import { errorResult, jsonResult, optionalPathSchema } from "./common.js";

export function registerSolidWorksReadTools(server: McpServer): void {
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
  registerReadOnlyWorker(
    "solidworks_list_mates",
    "List assembly mates",
    "Return mate feature names/types for an assembly.",
    "list_mates",
  );
  registerReadOnlyWorker(
    "solidworks_list_configurations",
    "List part configurations",
    "List configuration names for a part document.",
    "list_configurations",
  );

  const listDimensionsSchema = optionalPathSchema.extend({
    configuration: z.string().min(1).optional(),
  });

  server.registerTool(
    "solidworks_list_dimensions",
    {
      title: "List part dimensions",
      description: "List driving dimensions in a part (optionally for one configuration).",
      inputSchema: listDimensionsSchema,
      annotations: { readOnlyHint: true },
    },
    async (args: z.infer<typeof listDimensionsSchema>) => {
      try {
        const filePath = args.path ? assertAllowedPath(args.path) : undefined;
        if (!filePath) throw new Error("path is required for list_dimensions");
        return jsonResult(
          await runWorker({
            command: "list_dimensions",
            args: { path: filePath, configuration: args.configuration },
          }),
        );
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  registerReadOnlyWorker(
    "solidworks_list_interferences",
    "List assembly interferences",
    "Run interference detection on an assembly.",
    "list_interferences",
  );

  const transformReadSchema = optionalPathSchema.extend({
    component_name: z.string().min(1),
  });

  server.registerTool(
    "solidworks_get_component_transform",
    {
      title: "Get component transform",
      description: "Return 4x4 transform matrix for a named assembly component.",
      inputSchema: transformReadSchema,
      annotations: { readOnlyHint: true },
    },
    async (args: z.infer<typeof transformReadSchema>) => {
      try {
        const filePath = args.path ? assertAllowedPath(args.path) : undefined;
        if (!filePath) throw new Error("path is required");
        return jsonResult(
          await runWorker({
            command: "get_component_transform",
            args: { path: filePath, component_name: args.component_name },
          }),
        );
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  const persistRefGetSchema = optionalPathSchema.extend({
    component_name: z.string().min(1),
    ref: z.string().min(1),
    face_index: z.number().int().nonnegative().optional(),
  });

  server.registerTool(
    "solidworks_get_persist_reference",
    {
      title: "Get persist reference",
      description: "Capture a base64 persist reference for a component reference (stable across worker calls).",
      inputSchema: persistRefGetSchema,
      annotations: { readOnlyHint: true },
    },
    async (args: z.infer<typeof persistRefGetSchema>) => {
      try {
        const filePath = args.path ? assertAllowedPath(args.path) : undefined;
        if (!filePath) throw new Error("path is required");
        return jsonResult(
          await runWorker({
            command: "get_persist_reference",
            args: {
              path: filePath,
              component_name: args.component_name,
              ref: args.ref,
              face_index: args.face_index ?? 0,
            },
          }),
        );
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  const faceRaySchema = optionalPathSchema.extend({
    component_name: z.string().min(1),
    feature_name: z.string().min(1),
    face_index: z.number().int().nonnegative().optional(),
    mark: z.number().int().optional(),
    append: z.boolean().optional(),
  });

  server.registerTool(
    "solidworks_select_face_by_ray",
    {
      title: "Select feature face",
      description: "Select a feature face on a component by index (read-only selection state).",
      inputSchema: faceRaySchema,
      annotations: { readOnlyHint: true },
    },
    async (args: z.infer<typeof faceRaySchema>) => {
      try {
        const filePath = args.path ? assertAllowedPath(args.path) : undefined;
        if (!filePath) throw new Error("path is required");
        return jsonResult(
          await runWorker({
            command: "select_face_by_ray",
            args: {
              path: filePath,
              component_name: args.component_name,
              feature_name: args.feature_name,
              face_index: args.face_index ?? 0,
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

  server.registerTool(
    "solidworks_probe_feature_faces",
    {
      title: "Probe feature faces",
      description: "List face count/area/planarity for a component feature (ICE, extrusion, etc.).",
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

  const componentMassSchema = optionalPathSchema.extend({
    component_name: z.string().min(1),
  });

  server.registerTool(
    "solidworks_component_mass_properties",
    {
      title: "Component mass properties",
      description: "Per-component mass, COM, and inertia (requires resolved component).",
      inputSchema: componentMassSchema,
      annotations: { readOnlyHint: true },
    },
    async (args: z.infer<typeof componentMassSchema>) => {
      try {
        const filePath = args.path ? assertAllowedPath(args.path) : undefined;
        if (!filePath) throw new Error("path is required");
        return jsonResult(
          await runWorker({
            command: "component_mass_properties",
            args: { path: filePath, component_name: args.component_name },
          }),
        );
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "solidworks_assembly_diagnostics",
    {
      title: "Assembly diagnostics",
      description:
        "Counts fixed/float/lightweight/suppressed components, broken refs, and mates.",
      inputSchema: optionalPathSchema,
      annotations: { readOnlyHint: true },
    },
    async (args: z.infer<typeof optionalPathSchema>) => {
      try {
        const filePath = args.path ? assertAllowedPath(args.path) : undefined;
        if (!filePath) throw new Error("path is required");
        return jsonResult(
          await runWorker({ command: "assembly_diagnostics", args: { path: filePath } }),
        );
      } catch (error) {
        return errorResult(error);
      }
    },
  );
}

export { listMates };
