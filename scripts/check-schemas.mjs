import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

import { SCHEMA_BY_COMMAND, SCHEMA_KEYS } from "./schema-by-command.mjs";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

function parseRegistryCommands() {
  const text = readFileSync(
    path.join(root, "workers/SolidWorksComWorker/WorkerCommandRegistry.cs"),
    "utf8",
  );
  return [...new Set([...text.matchAll(/\["([a-z0-9_]+)"\]\s*=/g)].map((m) => m[1]))].sort();
}

function parseDestructiveCommands() {
  const text = readFileSync(path.join(root, "scripts/generate-manifest-from-registry.mjs"), "utf8");
  const block = text.match(/const DESTRUCTIVE = new Set\(\[([\s\S]*?)\]\);/);
  if (!block) {
    throw new Error("Could not parse DESTRUCTIVE set from generate-manifest-from-registry.mjs");
  }
  return [...block[1].matchAll(/"([a-z0-9_]+)"/g)].map((m) => m[1]).sort();
}

function parseSchemaMapKeys() {
  const text = readFileSync(path.join(root, "src/tool-registry.ts"), "utf8");
  const block = text.match(/export const SCHEMA_MAP = \{([\s\S]*?)\} as const/);
  if (!block) {
    throw new Error("Could not parse SCHEMA_MAP from src/tool-registry.ts");
  }
  return [...block[1].matchAll(/^\s+([A-Za-z0-9_]+):/gm)].map((m) => m[1]).sort();
}

function parseManifestTools() {
  const manifest = JSON.parse(readFileSync(path.join(root, "tools/manifest.json"), "utf8"));
  return manifest.tools ?? [];
}

const HAND_REGISTERED = new Set([
  "invoke",
  "batch_invoke",
  "status",
  "urdf_readiness",
  "add_urdf_frame",
]);

const registry = parseRegistryCommands();
const destructive = new Set(parseDestructiveCommands());
const schemaMapKeys = new Set(parseSchemaMapKeys());
const manifestTools = parseManifestTools();
const errors = [];

for (const schemaKey of SCHEMA_KEYS) {
  if (!schemaMapKeys.has(schemaKey)) {
    errors.push(`schema-by-command references unknown SCHEMA_MAP key: ${schemaKey}`);
  }
}

for (const [command, schemaKey] of Object.entries(SCHEMA_BY_COMMAND)) {
  if (!registry.includes(command)) {
    errors.push(`schema-by-command maps unknown registry command: ${command}`);
  }
  if (!schemaMapKeys.has(schemaKey)) {
    errors.push(`schema-by-command ${command} -> ${schemaKey} missing from SCHEMA_MAP`);
  }
}

const manifestCommands = manifestTools.map((tool) => tool.workerCommand);
for (const command of registry) {
  if (HAND_REGISTERED.has(command)) {
    continue;
  }
  if (!manifestCommands.includes(command)) {
    errors.push(`Registry command missing from manifest: ${command}`);
  }
  const expectedSchema = SCHEMA_BY_COMMAND[command] ?? "optionalPath";
  const tool = manifestTools.find((entry) => entry.workerCommand === command);
  if (tool && tool.schema !== expectedSchema) {
    errors.push(`Manifest schema drift for ${command}: expected ${expectedSchema}, got ${tool.schema}`);
  }
  if (destructive.has(command) && expectedSchema === "optionalPath") {
    errors.push(`Destructive command ${command} must not use optionalPath schema`);
  }
}

for (const tool of manifestTools) {
  if (tool.destructive && tool.schema === "optionalPath") {
    errors.push(`Manifest marks destructive tool ${tool.workerCommand} with optionalPath schema`);
  }
  if (tool.schema && !schemaMapKeys.has(tool.schema)) {
    errors.push(`Manifest tool ${tool.name} references unknown schema key: ${tool.schema}`);
  }
}

if (errors.length) {
  console.error("Schema coverage check failed:\n");
  for (const err of errors) {
    console.error(`- ${err}`);
  }
  process.exit(1);
}

console.log(
  JSON.stringify(
    {
      ok: true,
      mappedCommands: Object.keys(SCHEMA_BY_COMMAND).length,
      schemaKeys: SCHEMA_KEYS.length,
      manifestTools: manifestTools.length,
      destructiveMapped: [...destructive].filter((command) => SCHEMA_BY_COMMAND[command]).length,
    },
    null,
    2,
  ),
);
