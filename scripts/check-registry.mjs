#!/usr/bin/env node
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

function parseRegistryCommands() {
  const text = fs.readFileSync(
    path.join(root, "workers/SolidWorksComWorker/WorkerCommandRegistry.cs"),
    "utf8",
  );
  return [...new Set([...text.matchAll(/\["([a-z0-9_]+)"\]\s*=/g)].map((m) => m[1]))].sort();
}

function parseWorkerTsCommands() {
  const text = fs.readFileSync(path.join(root, "src/worker.ts"), "utf8");
  const block = text.match(/export type WorkerCommand =([\s\S]*?);/);
  if (!block) return [];
  return [...block[1].matchAll(/\|\s*"([a-z0-9_]+)"/g)].map((m) => m[1]).sort();
}

function parseScriptCommands() {
  const scriptsDir = path.join(root, "scripts");
  const commands = new Set();
  for (const file of fs.readdirSync(scriptsDir)) {
    if (!file.endsWith(".mjs") || file === "check-registry.mjs" || file === "validate-tools.mjs") continue;
    const text = fs.readFileSync(path.join(scriptsDir, file), "utf8");
    for (const match of text.matchAll(/runWorker\(\s*["'`]([a-z0-9_]+)["'`]/g)) {
      commands.add(match[1]);
    }
    for (const match of text.matchAll(/runWorker\(\{\s*command:\s*["'`]([a-z0-9_]+)["'`]/g)) {
      commands.add(match[1]);
    }
  }
  return [...commands].sort();
}

function parseManifestCommands() {
  const manifestPath = path.join(root, "tools/manifest.json");
  if (!fs.existsSync(manifestPath)) return [];
  const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
  return (manifest.tools ?? []).map((t) => t.workerCommand).sort();
}

const registry = parseRegistryCommands();
const workerTs = parseWorkerTsCommands();
const scripts = parseScriptCommands();
const manifest = parseManifestCommands();

const errors = [];

const missingInRegistry = scripts.filter((c) => !registry.includes(c));
if (missingInRegistry.length) {
  errors.push(`Scripts reference commands missing from WorkerCommandRegistry: ${missingInRegistry.join(", ")}`);
}

const missingInWorkerTs = registry.filter((c) => !workerTs.includes(c));
if (missingInWorkerTs.length) {
  errors.push(`Registry commands missing from src/worker.ts union: ${missingInWorkerTs.join(", ")}`);
}

const extraInWorkerTs = workerTs.filter((c) => !registry.includes(c));
if (extraInWorkerTs.length) {
  errors.push(`src/worker.ts union has unknown commands: ${extraInWorkerTs.join(", ")}`);
}

const manifestSet = new Set(manifest);
const missingInManifest = registry.filter((c) => !manifestSet.has(c) && ![
  "status",
  "set_custom_properties",
  "probe_feature_faces",
  "get_feature_box",
  "save_document",
  "align_component_to_feature",
  "mate_coincident",
  "mate_parallel",
  "torso_frame_build_mates",
].includes(c));
if (missingInManifest.length) {
  errors.push(`Registry commands missing from tools/manifest.json: ${missingInManifest.join(", ")}`);
}

if (errors.length) {
  console.error("Registry drift detected:\n");
  for (const err of errors) console.error(`- ${err}`);
  process.exit(1);
}

console.log(
  JSON.stringify(
    {
      ok: true,
      registryCount: registry.length,
      workerTsCount: workerTs.length,
      scriptCommandCount: scripts.length,
      manifestCount: manifest.length,
    },
    null,
    2,
  ),
);
