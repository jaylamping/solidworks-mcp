#!/usr/bin/env node
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

import { z } from "zod";

import { SCHEMAS, type SchemaRef } from "../src/tool-spec/catalog.js";

type DescriptionSource = "authored" | "derived";
type Tool = {
  name: string;
  workerCommand: string;
  tier: string;
  readOnly: boolean;
  destructive?: boolean;
  confirmRequired?: boolean;
  autoCheckpoint?: boolean;
  description: string;
  descriptionSource?: DescriptionSource;
  tags?: string[];
  domains?: string[];
  schema?: SchemaKey | "custom";
};
type JsonNode = {
  type?: string | string[];
  description?: string;
  enum?: unknown[];
  const?: unknown;
  default?: unknown;
  minimum?: number;
  maximum?: number;
  minLength?: number;
  items?: JsonNode;
  properties?: Record<string, JsonNode>;
};
type JsonObject = JsonNode & { required?: string[] };

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const manifestPath = path.join(root, "tools/manifest.json");
const outDir = path.join(root, "docs/api");
const checkOnly = process.argv.includes("--check");
const PATH_POLICY_FIELDS = new Set([
  "path",
  "part_path",
  "output_path",
  "output_part_path",
  "source_part_path",
  "assembly_path",
  "from_part_path",
  "to_part_path",
  "model_path",
  "component_path",
  "output_dir",
]);

function escapeCell(value: string): string {
  return value.replaceAll("|", "\\|").replaceAll("\n", " ");
}

function formatValue(value: unknown): string {
  if (value === undefined) return "-";
  if (typeof value === "string") return `\`${escapeCell(value)}\``;
  if (typeof value === "boolean" || typeof value === "number") return `\`${value}\``;
  return `\`${escapeCell(JSON.stringify(value))}\``;
}

function formatType(node: JsonNode): string {
  if (node.const !== undefined) return `const ${formatValue(node.const)}`;
  if (node.enum?.length) return node.enum.map(formatValue).join(", ");
  if (Array.isArray(node.type)) return node.type.join(" | ");
  if (node.type === "array") {
    return node.items?.type ? `${node.items.type}[]` : "array";
  }
  if (node.type === "object") return "object";
  return node.type ?? "unknown";
}

function fieldDescription(name: string, node: JsonNode): string {
  const description = node.description ?? `Value for ${name.replaceAll("_", " ")}.`;
  const policy = PATH_POLICY_FIELDS.has(name) ? " (allowed root)" : "";
  return escapeCell(`${description}${policy}`);
}

function renderParameters(schema: JsonObject): string {
  const properties = schema.properties ?? {};
  const required = new Set(schema.required ?? []);
  const rows = Object.entries(properties).map(([name, node]) => [
    `\`${name}\``,
    formatType(node),
    required.has(name) ? "yes" : "no",
    formatValue(node.default ?? node.const),
    fieldDescription(name, node),
  ]);
  const table = [
    "| Name | Type | Required | Default | Description |",
    "|------|------|----------|---------|-------------|",
    ...(rows.length
      ? rows.map((row) => `| ${row.join(" | ")} |`)
      : ["| - | - | no | - | No parameters. |"]),
  ];
  return `## Parameters\n\n${table.join("\n")}`;
}

function resolveSchema(key: Tool["schema"]): z.ZodTypeAny {
  if (!key || key === "custom") return SCHEMAS.optionalPath.value;
  const schema = SCHEMAS[key as keyof typeof SCHEMAS] as SchemaRef | undefined;
  if (!schema) throw new Error(`Unknown schema key in manifest: ${key}`);
  return schema.value;
}

function renderToolDoc(tool: Tool): string {
  const schema = z.toJSONSchema(resolveSchema(tool.schema)) as JsonObject;
  return `# ${tool.name}

${escapeCell(tool.description)}

| Field | Value |
|-------|-------|
| Worker command | \`${tool.workerCommand}\` |
| Tier | ${tool.tier} |
| Read only | ${tool.readOnly} |
| Destructive | ${tool.destructive ?? false} |
| Confirm required | ${tool.confirmRequired ?? false} |
| Description source | ${tool.descriptionSource ?? "derived"} |

${renderParameters(schema)}

## Tags

${(tool.tags ?? []).map((tag) => `- ${escapeCell(tag)}`).join("\n") || "- none"}

## Domains

${(tool.domains ?? []).map((domain) => `- ${escapeCell(domain)}`).join("\n") || "- none"}
`;
}

function main(): void {
  if (!fs.existsSync(manifestPath)) {
    throw new Error("tools/manifest.json missing. Run npm run generate:tools first.");
  }
  const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8")) as { tools?: Tool[] };
  const tools = manifest.tools ?? [];
  const expected = new Map(tools.map((tool) => [`${tool.name}.md`, `${renderToolDoc(tool)}\n`]));
  const actualFiles = new Set(fs.existsSync(outDir) ? fs.readdirSync(outDir).filter((file) => file.endsWith(".md")) : []);
  const stale = [...actualFiles].filter((file) => !expected.has(file));
  const changed = [...expected].filter(([file, content]) => {
    const actual = path.join(outDir, file);
    return !fs.existsSync(actual) || fs.readFileSync(actual, "utf8") !== content;
  });

  if (checkOnly) {
    if (stale.length || changed.length) {
      console.error(`Docs drift detected: ${changed.length} changed, ${stale.length} stale`);
      if (changed.length) console.error(`Changed: ${changed.map(([file]) => file).join(", ")}`);
      if (stale.length) console.error(`Stale: ${stale.join(", ")}`);
      process.exit(1);
    }
  } else {
    fs.mkdirSync(outDir, { recursive: true });
    for (const [file, content] of expected) fs.writeFileSync(path.join(outDir, file), content);
    for (const file of stale) fs.rmSync(path.join(outDir, file));
  }

  const authored = tools.filter((tool) => tool.descriptionSource === "authored").length;
  console.log(`${checkOnly ? "Checked" : "Wrote"} ${tools.length} tool docs (${authored} authored, ${tools.length - authored} derived)`);
}

main();
