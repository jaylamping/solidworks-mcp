import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";

import { prepareDocumentPath } from "../config.js";
import { runWorker } from "../worker.js";
import { errorResult, jsonResult, optionalPathSchema } from "./common.js";

const invokeSchema = optionalPathSchema.extend({
  target: z.enum(["app", "active_doc", "component", "persist_ref"]),
  component_name: z.string().min(1).optional(),
  persist_reference: z.string().min(1).optional(),
  member: z.string().min(1),
  args: z.array(z.unknown()).optional(),
  property: z.boolean().optional(),
});

const batchInvokeSchema = z.object({
  calls: z.array(invokeSchema).min(1).max(50),
});

export function registerSolidWorksInvokeTools(server: McpServer): void {
  server.registerTool(
    "solidworks_invoke",
    {
      title: "Invoke SolidWorks API (allowlisted)",
      description:
        "Call an allowlisted SolidWorks API member on app, active_doc, component, or persist_ref target. Read-only by default; writes require SOLIDWORKS_MCP_INVOKE_WRITE=true.",
      inputSchema: invokeSchema,
      annotations: { readOnlyHint: true },
    },
    async (args: z.infer<typeof invokeSchema>) => {
      try {
        const filePath = args.path ? prepareDocumentPath(args.path) : undefined;
        return jsonResult(
          await runWorker({
            command: "invoke",
            args: {
              path: filePath,
              target: args.target,
              component_name: args.component_name,
              persist_reference: args.persist_reference,
              member: args.member,
              args: args.args,
              property: args.property ?? false,
            },
          }),
        );
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "solidworks_batch_invoke",
    {
      title: "Batch invoke SolidWorks API",
      description:
        "Run multiple allowlisted invoke calls in one worker process (multi-step chains without cold-start per call).",
      inputSchema: batchInvokeSchema,
      annotations: { readOnlyHint: true },
    },
    async (args: z.infer<typeof batchInvokeSchema>) => {
      try {
        const calls = args.calls.map((call) => ({
          ...call,
          path: call.path ? prepareDocumentPath(call.path) : undefined,
        }));
        return jsonResult(await runWorker({ command: "batch_invoke", args: { calls } }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );
}
