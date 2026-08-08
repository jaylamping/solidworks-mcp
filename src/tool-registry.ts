import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";

import { assertAllowedPath, prepareDocumentPath } from "./config.js";
import { appendAuditEntry } from "./audit-log.js";
import { formatErrorForMcp } from "./errors.js";
import {
  TOOL_SPECS,
  type McpExposure,
  type ToolSafety,
  type ToolSpec,
  type ToolTier,
} from "./tool-spec/catalog.js";
import { runWorker } from "./worker.js";

const TIER_ORDER: ToolTier[] = ["core", "extended", "advanced", "debug"];
/** Existing-doc lookups — worker trusts open docs; disk opens still PathGuard'd. */
const DOCUMENT_PATH_FIELDS = new Set([
  "path",
  "part_path",
  "source_part_path",
  "assembly_path",
  "from_part_path",
  "to_part_path",
  "model_path",
  "component_path",
]);
/** New file / export destinations — must stay inside ALLOWED_ROOTS. */
const OUTPUT_PATH_FIELDS = new Set([
  "output_path",
  "output_part_path",
  "output_dir",
  "preview_path",
]);

type McpToolSpec = ToolSpec & { readonly exposure: McpExposure };

interface SafetyProjection {
  readonly readOnly: boolean;
  readonly destructive: boolean;
  readonly confirmRequired: boolean;
}

function isMcpTool(spec: ToolSpec): spec is McpToolSpec {
  return spec.exposure.kind === "mcp";
}

function projectSafety(safety: ToolSafety): SafetyProjection {
  switch (safety.kind) {
    case "read":
      return { readOnly: true, destructive: false, confirmRequired: false };
    case "modelMutation":
      return {
        readOnly: false,
        destructive: safety.destructive,
        confirmRequired: safety.destructive,
      };
    case "nonModelSideEffect":
      return {
        readOnly: false,
        destructive: safety.destructive,
        confirmRequired: safety.destructive,
      };
    default: {
      const exhaustive: never = safety;
      return exhaustive;
    }
  }
}

export type { ToolTier };

export function activeToolTier(): ToolTier | "all" {
  const raw = process.env.SOLIDWORKS_MCP_TOOL_TIER?.toLowerCase();
  if (!raw || raw === "all") {
    return "all";
  }
  if (raw === "core" || raw === "extended" || raw === "advanced" || raw === "debug") {
    return raw;
  }
  return "core";
}

function tierAllowed(toolTier: ToolTier, maxTier: ToolTier | "all"): boolean {
  if (maxTier === "all") {
    return true;
  }
  return TIER_ORDER.indexOf(toolTier) <= TIER_ORDER.indexOf(maxTier);
}

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

function prepareArgs(
  spec: McpToolSpec,
  args: Record<string, unknown>,
): Record<string, unknown> {
  const prepared = { ...args };
  for (const key of DOCUMENT_PATH_FIELDS) {
    const value = prepared[key];
    if (typeof value === "string") {
      prepared[key] = prepareDocumentPath(value);
    }
  }
  for (const key of OUTPUT_PATH_FIELDS) {
    const value = prepared[key];
    if (typeof value === "string") {
      prepared[key] = assertAllowedPath(value);
    }
  }

  const safety = projectSafety(spec.safety);
  if (safety.confirmRequired && prepared.confirm !== true) {
    throw new Error(`Destructive tool ${spec.exposure.name} requires confirm: true`);
  }

  return prepared;
}

function audit(
  spec: McpToolSpec,
  ok: boolean,
  workerArgs: Record<string, unknown>,
  data?: unknown,
  error?: unknown,
): void {
  const recordData = data && typeof data === "object"
    ? data as {
        ok?: unknown;
        preCheckpoint?: { checkpointPath?: string };
        checkpoint?: { checkpointPath?: string };
      }
    : undefined;
  const checkpointPath =
    typeof recordData?.preCheckpoint?.checkpointPath === "string"
      ? recordData.preCheckpoint.checkpointPath
      : typeof recordData?.checkpoint?.checkpointPath === "string"
        ? recordData.checkpoint.checkpointPath
        : undefined;

  appendAuditEntry({
    tool: spec.exposure.name,
    command: spec.implementation.command,
    ok: typeof recordData?.ok === "boolean" ? recordData.ok : ok,
    destructive: projectSafety(spec.safety).destructive,
    path: typeof workerArgs.path === "string" ? workerArgs.path : undefined,
    checkpointPath,
    error: error instanceof Error ? error.message : error === undefined ? undefined : String(error),
  });
}

export function registerSolidWorksTools(server: McpServer): void {
  const maxTier = activeToolTier();
  const registered = TOOL_SPECS
    .filter(isMcpTool)
    .filter((spec) => tierAllowed(spec.exposure.tier, maxTier));

  for (const spec of registered) {
    const safety = projectSafety(spec.safety);
    server.registerTool(
      spec.exposure.name,
      {
        title: spec.exposure.name.replaceAll("_", " "),
        description: spec.exposure.description,
        inputSchema: spec.exposure.input.value,
        annotations: { readOnlyHint: safety.readOnly },
      },
      async (args: unknown) => {
        const record = (args ?? {}) as Record<string, unknown>;
        try {
          const workerArgs = prepareArgs(spec, record);
          const data = await runWorker({ command: spec.implementation.command, args: workerArgs });
          if (!safety.readOnly) {
            audit(spec, true, workerArgs, data);
          }
          return jsonResult(data);
        } catch (error) {
          if (!safety.readOnly) {
            audit(spec, false, record, undefined, error);
          }
          return errorResult(error);
        }
      },
    );
  }

  server.registerTool(
    "solidworks_search_tools",
    {
      title: "Search SolidWorks MCP tools",
      description: "Search registered tools by name, tag, or domain. Respects SOLIDWORKS_MCP_TOOL_TIER.",
      inputSchema: z.object({
        query: z.string().min(1),
        limit: z.number().int().min(1).max(50).optional(),
      }),
      annotations: { readOnlyHint: true },
    },
    async (args: { query: string; limit?: number }) => {
      const query = args.query.toLowerCase();
      const limit = args.limit ?? 20;
      const matches = registered
        .filter(
          (spec) =>
            spec.exposure.name.toLowerCase().includes(query)
            || spec.exposure.description.toLowerCase().includes(query)
            || spec.exposure.tags.some((tag) => tag.toLowerCase().includes(query))
            || spec.exposure.domains.some((domain) => domain.toLowerCase().includes(query)),
        )
        .slice(0, limit)
        .map((spec) => {
          const safety = projectSafety(spec.safety);
          return {
            name: spec.exposure.name,
            workerCommand: spec.implementation.command,
            tier: spec.exposure.tier,
            readOnly: safety.readOnly,
            destructive: safety.destructive,
            description: spec.exposure.description,
          };
        });

      return jsonResult({ query: args.query, tier: maxTier, count: matches.length, tools: matches });
    },
  );
}
