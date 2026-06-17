import { readFileSync } from "node:fs";
import path from "node:path";

import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";

import { assertAllowedPath, packageRoot } from "./config.js";
import { appendAuditEntry } from "./audit-log.js";
import { formatErrorForMcp } from "./errors.js";
import * as assemblySchemas from "./schemas/assembly.js";
import * as documentSchemas from "./schemas/document.js";
import * as mateSchemas from "./schemas/mate.js";
import * as partSchemas from "./schemas/part.js";
import { runWorker, type WorkerCommand } from "./worker.js";

export type ToolTier = "core" | "extended" | "advanced" | "debug";

export interface ToolManifestEntry {
  name: string;
  workerCommand: WorkerCommand;
  tier: ToolTier;
  readOnly: boolean;
  destructive?: boolean;
  confirmRequired?: boolean;
  description: string;
  tags?: string[];
  domains?: string[];
  schema?: "optionalPath" | "open" | "export" | "mateRefs" | "mateLimitAngle" | "diagnose" | "explainError" | "checkpoint" | "confirmPath" | "custom";
}

interface ToolManifest {
  version: string;
  tools: ToolManifestEntry[];
}

const SCHEMA_MAP = {
  optionalPath: documentSchemas.optionalPathSchema,
  componentName: assemblySchemas.componentNameSchema,
  featureProbe: assemblySchemas.featureProbeSchema,
  partFeatureProbe: assemblySchemas.partFeatureProbeSchema,
  persistRef: assemblySchemas.persistRefSchema,
  align: assemblySchemas.alignSchema,
  open: documentSchemas.openSchema,
  export: documentSchemas.exportSchema,
  mateRefs: mateSchemas.mateRefsSchema,
  mateLimitAngle: mateSchemas.mateLimitAngleSchema,
  mateTry: mateSchemas.mateTrySchema,
  diagnose: documentSchemas.diagnoseSchema,
  explainError: documentSchemas.explainErrorSchema,
  checkpoint: documentSchemas.checkpointSchema,
  confirmPath: documentSchemas.confirmPathSchema,
} as const;

function manifestPath(): string {
  return path.join(packageRoot(), "tools/manifest.json");
}

export function loadManifest(): ToolManifest {
  return JSON.parse(readFileSync(manifestPath(), "utf8")) as ToolManifest;
}

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

const TIER_ORDER: ToolTier[] = ["core", "extended", "advanced", "debug"];

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

function resolveSchema(entry: ToolManifestEntry): z.ZodTypeAny {
  if (!entry.schema || entry.schema === "custom") {
    return documentSchemas.optionalPathSchema;
  }
  return SCHEMA_MAP[entry.schema];
}

function prepareArgs(entry: ToolManifestEntry, args: Record<string, unknown>): Record<string, unknown> {
  const prepared = { ...args };
  for (const key of ["path", "part_path", "output_path", "source_part_path", "assembly_path"]) {
    const value = prepared[key];
    if (typeof value === "string") {
      prepared[key] = assertAllowedPath(value);
    }
  }

  if (entry.confirmRequired && prepared.confirm !== true) {
    throw new Error(`Destructive tool ${entry.name} requires confirm: true`);
  }

  return prepared;
}

export function registerAllTools(server: McpServer): void {
  const manifest = loadManifest();
  const maxTier = activeToolTier();
  const registered = manifest.tools.filter((tool) => tierAllowed(tool.tier, maxTier));

  for (const entry of registered) {
    const inputSchema = resolveSchema(entry);
    server.registerTool(
      entry.name,
      {
        title: entry.name.replaceAll("_", " "),
        description: entry.description,
        inputSchema,
        annotations: { readOnlyHint: entry.readOnly },
      },
      async (args: unknown) => {
        try {
          const record = (args ?? {}) as Record<string, unknown>;
          const workerArgs = prepareArgs(entry, record);
          const data = await runWorker({ command: entry.workerCommand, args: workerArgs });
          if (!entry.readOnly) {
            appendAuditEntry({
              tool: entry.name,
              command: entry.workerCommand,
              ok: true,
              destructive: entry.destructive,
              path: typeof workerArgs.path === "string" ? workerArgs.path : undefined,
            });
          }
          return jsonResult(data);
        } catch (error) {
          if (!entry.readOnly) {
            appendAuditEntry({
              tool: entry.name,
              command: entry.workerCommand,
              ok: false,
              destructive: entry.destructive,
              error: error instanceof Error ? error.message : String(error),
            });
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
      const q = args.query.toLowerCase();
      const limit = args.limit ?? 20;
      const matches = registered
        .filter(
          (tool) =>
            tool.name.toLowerCase().includes(q)
            || tool.description.toLowerCase().includes(q)
            || (tool.tags ?? []).some((tag) => tag.toLowerCase().includes(q))
            || (tool.domains ?? []).some((domain) => domain.toLowerCase().includes(q)),
        )
        .slice(0, limit)
        .map((tool) => ({
          name: tool.name,
          workerCommand: tool.workerCommand,
          tier: tool.tier,
          readOnly: tool.readOnly,
          destructive: tool.destructive ?? false,
          description: tool.description,
        }));

      return jsonResult({ query: args.query, tier: maxTier, count: matches.length, tools: matches });
    },
  );
}
