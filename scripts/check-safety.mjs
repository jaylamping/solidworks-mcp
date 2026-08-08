#!/usr/bin/env node
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

import {
  manifestFromCatalog,
  policyFromCatalog,
  toolSpecs,
  validateToolSpecs,
} from "./generate-tools.mjs";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const manifestPath = path.join(root, "tools/manifest.json");
const policyPath = path.join(root, "tools/generated/command-safety-policy.json");
const forbiddenFiles = [
  "scripts/schema-by-command.mjs",
  "scripts/description-by-command.mjs",
  "scripts/generate-manifest-from-registry.mjs",
  "scripts/tool-spec-source.mjs",
];
const scanRoots = ["scripts", "src", "workers/SolidWorksComWorker"];
const legacyPatterns = [
  ["SCHEMA_BY_COMMAND declaration", /export\s+const\s+SCHEMA_BY_COMMAND/],
  ["JavaScript DESTRUCTIVE set", /const\s+DESTRUCTIVE\s*=\s*new\s+Set/],
  ["JavaScript READ_ONLY set", /const\s+READ_ONLY\s*=\s*new\s+Set/],
  ["JavaScript CORE set", /const\s+CORE\s*=\s*new\s+Set/],
  ["C# destructive set", /HashSet<string>\s+DestructiveCommands/],
  ["C# auto-checkpoint set", /HashSet<string>\s+AutoCheckpointCommands/],
  ["C# selection binding table", /SelectionCommandBindings\s*=\s*new/],
];

function walkFiles(relativeRoot) {
  const absoluteRoot = path.join(root, relativeRoot);
  if (!fs.existsSync(absoluteRoot)) {
    return [];
  }
  const entries = fs.readdirSync(absoluteRoot, { withFileTypes: true });
  return entries.flatMap((entry) => {
    const relativePath = path.join(relativeRoot, entry.name);
    if (entry.isDirectory()) {
      if (
        entry.name === "generated"
        || entry.name === "node_modules"
        || entry.name === "dist"
        || entry.name === "bin"
        || entry.name === "obj"
        || entry.name === ".git"
      ) {
        return [];
      }
      return walkFiles(relativePath);
    }
    if (!/\.(mjs|ts|cs)$/.test(entry.name)) {
      return [];
    }
    return [relativePath];
  });
}

function compareJsonArtifact(filePath, expected, errors) {
  const actual = fs.existsSync(filePath) ? fs.readFileSync(filePath, "utf8") : "";
  if (actual !== `${JSON.stringify(expected, null, 2)}\n`) {
    errors.push(`generated safety artifact is stale: ${path.relative(root, filePath)}`);
  }
}

const errors = [...validateToolSpecs(toolSpecs)];

for (const relativePath of forbiddenFiles) {
  if (fs.existsSync(path.join(root, relativePath))) {
    errors.push(`legacy metadata authority still exists: ${relativePath}`);
  }
}

for (const relativePath of scanRoots.flatMap(walkFiles)) {
  if (path.basename(relativePath) === "check-safety.mjs") {
    continue;
  }
  const text = fs.readFileSync(path.join(root, relativePath), "utf8");
  for (const [label, pattern] of legacyPatterns) {
    if (pattern.test(text)) {
      errors.push(`${label} found in ${relativePath}`);
    }
  }
}

const expectedManifest = manifestFromCatalog();
const actualManifest = fs.existsSync(manifestPath)
  ? JSON.parse(fs.readFileSync(manifestPath, "utf8"))
  : { tools: [] };
const actualByCommand = new Map(
  (actualManifest.tools ?? []).map((entry) => [entry.workerCommand, entry]),
);

for (const expected of expectedManifest.tools) {
  const actual = actualByCommand.get(expected.workerCommand);
  if (!actual) {
    errors.push(`active manifest is missing ${expected.workerCommand}`);
    continue;
  }
  for (const field of ["readOnly", "destructive", "confirmRequired", "autoCheckpoint"]) {
    if (actual[field] !== expected[field]) {
      errors.push(
        `active manifest safety drift for ${expected.workerCommand}: ${field}`,
      );
    }
  }
}

compareJsonArtifact(policyPath, policyFromCatalog(), errors);

if (errors.length) {
  console.error(`ToolSpec safety check failed:\n- ${errors.join("\n- ")}`);
  process.exit(1);
}

const projectedSafety = toolSpecs.filter(
  (spec) => spec.safety.kind !== "read" && spec.safety.destructive,
).length;
const autoCheckpoint = toolSpecs.filter(
  (spec) => spec.safety.kind === "modelMutation",
).length;

console.log(
  JSON.stringify(
    {
      ok: true,
      catalogCommands: toolSpecs.length,
      mcpTools: expectedManifest.tools.length,
      destructiveTools: projectedSafety,
      autoCheckpointTools: autoCheckpoint,
      legacyListGuard: "enabled",
    },
    null,
    2,
  ),
);
