#!/usr/bin/env node
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

import {
  manifestFromCatalog,
  policyFromCatalog,
  sources,
  toolSpecs,
  validateToolSpecs,
} from "./generate-tools.mjs";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const manifestPath = path.join(root, "tools/generated/tool-spec-manifest.json");
const policyPath = path.join(root, "tools/generated/command-safety-policy.json");

const diagnostics = validateToolSpecs(toolSpecs, sources);
const expectedManifest = `${JSON.stringify(manifestFromCatalog(), null, 2)}\n`;
const expectedPolicy = `${JSON.stringify(policyFromCatalog(), null, 2)}\n`;
const legacyDestructiveDrift = [
  ...[...sources.legacyDestructive].filter((command) => !sources.csharpDestructive.has(command)),
  ...[...sources.csharpDestructive].filter((command) => !sources.legacyDestructive.has(command)),
].sort();

function manifestMetadata(entry) {
  return {
    name: entry.name,
    workerCommand: entry.workerCommand,
    tier: entry.tier,
    description: entry.description,
    ...(entry.descriptionSource ? { descriptionSource: entry.descriptionSource } : {}),
    tags: entry.tags ?? [],
    domains: entry.domains ?? [],
    schema: entry.schema,
  };
}

const activeManifestByCommand = new Map(
  sources.manifest.map((entry) => [entry.workerCommand, entry]),
);
for (const expectedEntry of manifestFromCatalog().tools) {
  const activeEntry = activeManifestByCommand.get(expectedEntry.workerCommand);
  if (!activeEntry) {
    diagnostics.push(`active manifest is missing ${expectedEntry.workerCommand}`);
    continue;
  }
  if (
    JSON.stringify(manifestMetadata(activeEntry))
    !== JSON.stringify(manifestMetadata(expectedEntry))
  ) {
    diagnostics.push(`active manifest metadata drift: ${expectedEntry.workerCommand}`);
  }
}

if (fs.readFileSync(manifestPath, "utf8") !== expectedManifest) {
  diagnostics.push("tools/generated/tool-spec-manifest.json is stale; run npm run generate:tools");
}
if (fs.readFileSync(policyPath, "utf8") !== expectedPolicy) {
  diagnostics.push("tools/generated/command-safety-policy.json is stale; run npm run generate:tools");
}
if (legacyDestructiveDrift.length) {
  diagnostics.push(`JS/C# destructive drift: ${legacyDestructiveDrift.join(", ")}`);
}

if (diagnostics.length) {
  console.error(`ToolSpec safety check failed:\n- ${diagnostics.join("\n- ")}`);
  process.exit(1);
}

console.log(
  JSON.stringify(
    {
      ok: true,
      catalogCommands: toolSpecs.length,
      mcpTools: toolSpecs.filter((spec) => spec.exposure.kind === "mcp").length,
      csharpDestructive: sources.csharpDestructive.size,
      csharpAutoCheckpoint: sources.csharpAutoCheckpoint.size,
      legacyDestructiveDrift: [],
      legacyListGuard: "skipped: scaffold-only until atomic cutover",
    },
    null,
    2,
  ),
);
