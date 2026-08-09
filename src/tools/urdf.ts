import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";

import { assertAllowedPath, prepareDocumentPath } from "../config.js";
import {
  addUrdfFrameSchema,
  generateUrdfSchema,
  urdfReadinessSchema,
} from "../schemas/urdf.js";
import { writeUrdfFromPackage } from "../urdf/generate.js";
import { runWorker } from "../worker.js";
import { errorResult, jsonResult } from "./common.js";

export function registerUrdfTools(server: McpServer): void {
  server.registerTool(
    "solidworks_urdf_readiness",
    {
      title: "URDF readiness",
      description:
        "Walk the active document (or path) assembly tree in one COM session and report presence of required named reference geometry (default urdf_link_frame, joint_axis). Includes limit/angle mate names on assemblies.",
      inputSchema: urdfReadinessSchema,
      annotations: { readOnlyHint: true },
    },
    async (args: z.infer<typeof urdfReadinessSchema>) => {
      try {
        const filePath = args.path ? prepareDocumentPath(args.path) : undefined;
        return jsonResult(
          await runWorker({
            command: "urdf_readiness",
            args: {
              path: filePath,
              required_refs: args.required_refs,
              use_selection: args.use_selection,
              selection_index: args.selection_index,
            },
          }),
        );
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "solidworks_add_urdf_frame",
    {
      title: "Add URDF link frame",
      description:
        "Create a named coordinate system on a part (default urdf_link_frame) from origin and axis plane references. Prefer save:false during prep; requires confirm: true.",
      inputSchema: addUrdfFrameSchema,
      annotations: { readOnlyHint: false },
    },
    async (args: z.infer<typeof addUrdfFrameSchema>) => {
      try {
        const filePath = assertAllowedPath(args.path);
        return jsonResult(
          await runWorker({
            command: "add_urdf_frame",
            args: {
              path: filePath,
              name: args.name,
              origin_x_m: args.origin_x_m,
              origin_y_m: args.origin_y_m,
              origin_z_m: args.origin_z_m,
              x_axis_ref: args.x_axis_ref,
              y_axis_ref: args.y_axis_ref,
              replace_existing: args.replace_existing,
              save: args.save ?? false,
              confirm: true,
            },
          }),
        );
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "solidworks_generate_urdf",
    {
      title: "Generate URDF",
      description:
        "Generate a URDF file from a CadUrdfPackage directory (package.json + meshes). Does not call SolidWorks.",
      inputSchema: generateUrdfSchema,
      annotations: { readOnlyHint: false },
    },
    async (args: z.infer<typeof generateUrdfSchema>) => {
      try {
        const packagePath = assertAllowedPath(args.package_path);
        const urdfOutputPath = args.urdf_output_path
          ? assertAllowedPath(args.urdf_output_path)
          : undefined;
        const result = writeUrdfFromPackage({
          packagePath,
          urdfOutputPath,
          meshUriPrefix: args.mesh_uri_prefix,
        });
        return jsonResult(result);
      } catch (error) {
        return errorResult(error);
      }
    },
  );
}
