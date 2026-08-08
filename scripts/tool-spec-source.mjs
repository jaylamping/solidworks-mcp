import fs from "node:fs";
import path from "node:path";

const HAND_REGISTERED = new Set([
  "invoke",
  "batch_invoke",
  "status",
  "urdf_readiness",
  "add_urdf_frame",
]);

const SELECTION_SOURCE_BY_FIELD = {
  ComponentName: "selectedComponentName",
  ComponentPath: "selectedComponentPath",
  FeatureName: "selectedFeatureName",
  PlaneName: "selectedPlaneName",
  BodyName: "selectedBodyName",
  PersistReference: "selectedPersistReference",
  RefName: "selectedRefName",
};

function read(root, relativePath) {
  return fs.readFileSync(path.join(root, relativePath), "utf8");
}

function parseRegistry(text) {
  return [...text.matchAll(/\["([a-z0-9_]+)"\]\s*=\s*([A-Za-z0-9_]+)/g)].map(
    ([, command, handler]) => ({ command, handler }),
  );
}

function parseStringSet(text, name) {
  const block = text.match(new RegExp(`${name}\\s*=\\s*[\\s\\S]*?\\{([\\s\\S]*?)\\};`));
  if (!block) {
    throw new Error(`Could not parse ${name}`);
  }
  return new Set([...block[1].matchAll(/"([a-z0-9_]+)"/g)].map(([, value]) => value));
}

function parseJavaScriptSet(text, name) {
  const block = text.match(new RegExp(`${name}\\s*=\\s*new Set\\(\\[([\\s\\S]*?)\\]\\);`));
  if (!block) {
    throw new Error(`Could not parse ${name}`);
  }
  return new Set([...block[1].matchAll(/"([a-z0-9_]+)"/g)].map(([, value]) => value));
}

function parseSchemaBindings(text) {
  const block = text.match(/export const SCHEMA_MAP = \{([\s\S]*?)\} as const/);
  if (!block) {
    throw new Error("Could not parse SCHEMA_MAP");
  }

  return Object.fromEntries(
    [...block[1].matchAll(/^\s+([A-Za-z0-9_]+):\s*([A-Za-z0-9_]+)\.([A-Za-z0-9_]+),/gm)].map(
      ([, key, namespace, schema]) => [key, { namespace, schema }],
    ),
  );
}

function parseSelectionBindings(text) {
  const block = text.match(
    /SelectionCommandBindings\s*=\s*new[\s\S]*?\n\s*};\s*\n\s*private static readonly string\[\]/,
  );
  if (!block) {
    throw new Error("Could not parse SelectionCommandBindings");
  }

  const bindings = {};
  for (const [, command, entries] of block[0].matchAll(
    /\["([a-z0-9_]+)"\]\s*=\s*(\[[\s\S]*?\])\s*,/g,
  )) {
    bindings[command] = [
      ...entries.matchAll(
        /new\("([^"]+)",\s*SelectionFieldKind\.([A-Za-z]+),\s*(\d+)\)/g,
      ),
    ].map(([, targetArg, field, selectionIndex]) => {
      const source = SELECTION_SOURCE_BY_FIELD[field];
      if (!source) {
        throw new Error(`Unknown SelectionFieldKind ${field} for ${command}`);
      }
      return { targetArg, source, selectionIndex: Number(selectionIndex) };
    });
  }
  return bindings;
}

function parseSchemaMapFromManifest(root, manifest) {
  const bindings = parseSchemaBindings(read(root, "src/tool-registry.ts"));
  const result = new Map();
  for (const entry of manifest) {
    if (entry.schema && entry.schema !== "custom" && bindings[entry.schema]) {
      result.set(entry.schema, bindings[entry.schema]);
    }
  }
  return Object.fromEntries(result);
}

function safetyForCommand(command, manifestEntry, sources) {
  const destructive = sources.csharpDestructive.has(command);
  if (manifestEntry?.readOnly || command === "status" || command === "urdf_readiness") {
    return { kind: "read" };
  }
  if (sources.csharpAutoCheckpoint.has(command)) {
    return { kind: "modelMutation", destructive };
  }
  return {
    kind: "nonModelSideEffect",
    destructive,
    rationale: "The current worker safety policy does not auto-checkpoint this command.",
  };
}

function quote(value) {
  return JSON.stringify(value);
}

function renderStringArray(values) {
  return `[${values.map(quote).join(", ")}]`;
}

function renderSelection(selection) {
  if (!selection) {
    return "noSelection";
  }
  const bindings = selection.map(
    ({ targetArg, source, selectionIndex }) =>
      `{ targetArg: ${quote(targetArg)}, source: ${quote(source)}, selectionIndex: ${selectionIndex} }`,
  );
  return `bindSelection([${bindings.join(", ")}])`;
}

export function readCurrentSources(root) {
  const manifest = JSON.parse(read(root, "tools/manifest.json")).tools ?? [];
  const manifestByCommand = new Map(manifest.map((entry) => [entry.workerCommand, entry]));
  const registryCommands = parseRegistry(read(root, "workers/SolidWorksComWorker/WorkerCommandRegistry.cs"));
  const csharpSafety = read(root, "workers/SolidWorksComWorker/Interop/CommandSafety.cs");
  const manifestGenerator = read(root, "scripts/generate-manifest-from-registry.mjs");
  const selection = parseSelectionBindings(
    read(root, "workers/SolidWorksComWorker/Interop/SelectionContext.cs"),
  );

  const schemaByCommandText = read(root, "scripts/schema-by-command.mjs");
  const schemaByCommandBlock = schemaByCommandText.match(
    /export const SCHEMA_BY_COMMAND = \{([\s\S]*?)\};/,
  );
  if (!schemaByCommandBlock) {
    throw new Error("Could not parse SCHEMA_BY_COMMAND");
  }
  const schemaByCommand = Object.fromEntries(
    [...schemaByCommandBlock[1].matchAll(/^\s+([a-z0-9_]+):\s*"([^"]+)",/gm)].map(
      ([, command, schema]) => [command, schema],
    ),
  );

  const sources = {
    manifest,
    manifestByCommand,
    registryCommands,
    csharpDestructive: parseStringSet(csharpSafety, "DestructiveCommands"),
    csharpAutoCheckpoint: parseStringSet(csharpSafety, "AutoCheckpointCommands"),
    legacyDestructive: parseJavaScriptSet(manifestGenerator, "DESTRUCTIVE"),
    schemaByCommand,
    selection,
  };
  sources.schemaBindings = parseSchemaMapFromManifest(root, manifest);
  return sources;
}

export function renderCatalog(sources) {
  const namespaces = [...new Set(Object.values(sources.schemaBindings).map(({ namespace }) => namespace))].sort();
  const schemaEntries = Object.entries(sources.schemaBindings).sort(([a], [b]) => a.localeCompare(b));
  const lines = [
    "// Generated by npm run generate:tools during the Unit 2 bootstrap. Edit the catalog after cutover.",
    'import { z } from "zod";',
    'import type { WorkerCommand } from "../generated/worker-command.js";',
    ...namespaces.map((namespace) => `import * as ${namespace} from "../schemas/${namespace.replace("Schemas", "")}.js";`),
    "",
    'export type ToolTier = "core" | "extended" | "advanced" | "debug";',
    "",
    "export interface SchemaRef<K extends string = string, T extends z.ZodTypeAny = z.ZodTypeAny> {",
    "  readonly key: K;",
    "  readonly value: T;",
    "}",
    "",
    "export function schemaRef<const K extends string, T extends z.ZodTypeAny>(",
    "  key: K,",
    "  value: T,",
    "): SchemaRef<K, T> {",
    "  return { key, value };",
    "}",
    "",
    "export type ToolSafety =",
    '  | { readonly kind: "read" }',
    "  | {",
    '      readonly kind: "modelMutation";',
    "      readonly destructive: boolean;",
    '      readonly checkpoint: "auto";',
    "    }",
    "  | {",
    '      readonly kind: "nonModelSideEffect";',
    "      readonly destructive: boolean;",
    '      readonly checkpoint: "notApplicable";',
    "      readonly rationale: string;",
    "    };",
    "",
    "export const readSafety = (): ToolSafety => ({ kind: \"read\" });",
    "",
    "export function modelMutation(args: { readonly destructive: boolean }): ToolSafety {",
    '  return { kind: "modelMutation", destructive: args.destructive, checkpoint: "auto" };',
    "}",
    "",
    "export function nonModelSideEffect(args: { readonly destructive: boolean; readonly rationale: string }): ToolSafety {",
    "  if (!args.rationale.trim()) {",
    '    throw new Error("nonModelSideEffect requires a rationale");',
    "  }",
    '  return { kind: "nonModelSideEffect", destructive: args.destructive, checkpoint: "notApplicable", rationale: args.rationale };',
    "}",
    "",
    "export type SelectionSource =",
    '  | "selectedComponentName"',
    '  | "selectedComponentPath"',
    '  | "selectedFeatureName"',
    '  | "selectedPlaneName"',
    '  | "selectedBodyName"',
    '  | "selectedPersistReference"',
    '  | "selectedRefName";',
    "",
    "export interface SelectionBinding {",
    "  readonly targetArg: string;",
    "  readonly source: SelectionSource;",
    "  readonly selectionIndex?: 1 | 2;",
    "}",
    "",
    "export type SelectionPolicy =",
    '  | { readonly kind: "none" }',
    "  | {",
    '      readonly kind: "bindings";',
    '      readonly when: "use_selection";',
    "      readonly bindings: readonly SelectionBinding[];",
    "    };",
    "",
    "export const noSelection: SelectionPolicy = { kind: \"none\" };",
    "",
    "export function bindSelection(bindings: readonly SelectionBinding[]): SelectionPolicy {",
    '  return { kind: "bindings", when: "use_selection", bindings };',
    "}",
    "",
    "export type Implementation = {",
    '  readonly kind: "worker";',
    "  readonly command: WorkerCommand;",
    "  readonly csharpHandler: `${string}.${string}`;",
    "};",
    "",
    "export type McpExposure = {",
    '  readonly kind: "mcp";',
    '  readonly name: `solidworks_${string}`;',
    "  readonly tier: ToolTier;",
    "  readonly description: string;",
    "  readonly descriptionSource?: \"authored\" | \"derived\";",
    "  readonly input: SchemaRef;",
    "  readonly tags: readonly string[];",
    "  readonly domains: readonly string[];",
    "};",
    "",
    'export type Exposure = McpExposure | { readonly kind: "workerOnly" };',
    "",
    "export const workerOnly = (): Exposure => ({ kind: \"workerOnly\" });",
    "",
    "export function worker(command: WorkerCommand, csharpHandler: `${string}.${string}`): Implementation {",
    "  return { kind: \"worker\", command, csharpHandler };",
    "}",
    "",
    "export function mcp(exposure: Omit<McpExposure, \"kind\">): McpExposure {",
    '  return { kind: "mcp", ...exposure };',
    "}",
    "",
    "export interface ToolSpec {",
    "  readonly implementation: Implementation;",
    "  readonly exposure: Exposure;",
    "  readonly safety: ToolSafety;",
    "  readonly selection: SelectionPolicy;",
    "}",
    "",
    "export function tool<const T extends ToolSpec>(spec: T): T {",
    "  return spec;",
    "}",
    "",
    "export const SCHEMAS = {",
    ...schemaEntries.map(
      ([key, { namespace, schema }]) => `  ${key}: schemaRef(${quote(key)}, ${namespace}.${schema}),`,
    ),
    "} as const;",
    "",
    "export const TOOL_SPECS = [",
  ];

  for (const { command, handler } of sources.registryCommands) {
    const manifestEntry = sources.manifestByCommand.get(command);
    const safety = safetyForCommand(command, manifestEntry, sources);
    const selection = sources.selection[command];
    const implementation = `worker(${quote(command)}, ${quote(`Program.${handler}`)})`;
    lines.push("  tool({");
    lines.push(`    implementation: ${implementation},`);
    if (manifestEntry) {
      const schemaKey = manifestEntry.schema ?? sources.schemaByCommand[command] ?? "optionalPath";
      lines.push("    exposure: mcp({");
      lines.push(`      name: ${quote(manifestEntry.name)},`);
      lines.push(`      tier: ${quote(manifestEntry.tier)},`);
      lines.push(`      description: ${quote(manifestEntry.description)},`);
      if (manifestEntry.descriptionSource) {
        lines.push(`      descriptionSource: ${quote(manifestEntry.descriptionSource)},`);
      }
      lines.push(`      input: SCHEMAS[${quote(schemaKey)}],`);
      lines.push(`      tags: ${renderStringArray(manifestEntry.tags ?? [])},`);
      lines.push(`      domains: ${renderStringArray(manifestEntry.domains ?? [])},`);
      lines.push("    }),");
    } else {
      lines.push("    exposure: workerOnly(),");
    }
    if (safety.kind === "read") {
      lines.push("    safety: readSafety(),");
    } else if (safety.kind === "modelMutation") {
      lines.push(`    safety: modelMutation({ destructive: ${safety.destructive} }),`);
    } else {
      lines.push(
        `    safety: nonModelSideEffect({ destructive: ${safety.destructive}, rationale: ${quote(safety.rationale)} }),`,
      );
    }
    lines.push(`    selection: ${renderSelection(selection)},`);
    lines.push("  }),");
  }

  lines.push("] as const satisfies readonly ToolSpec[];", "");
  return `${lines.join("\n")}\n`;
}

export function isHandRegistered(command) {
  return HAND_REGISTERED.has(command);
}
