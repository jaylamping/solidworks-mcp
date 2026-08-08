import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

import { TOOL_SPECS } from "../src/tool-spec/catalog.ts";
import { WORKER_COMMANDS } from "../src/generated/worker-command.ts";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const registryPath = path.join(root, "workers/SolidWorksComWorker/WorkerCommandRegistry.cs");

function parseRegistryEntries() {
  const text = fs.readFileSync(registryPath, "utf8");
  return [...text.matchAll(/\["([a-z0-9_]+)"\]\s*=\s*([A-Za-z0-9_]+)/g)]
    .map(([, command, handler]) => ({ command, handler }))
    .sort((left, right) => left.command.localeCompare(right.command));
}

function parseScriptCommands() {
  const commands = new Set();
  for (const file of fs.readdirSync(path.join(root, "scripts"))) {
    if (!file.endsWith(".mjs") || file === "check-registry.mjs" || file === "validate-tools.mjs") {
      continue;
    }
    const text = fs.readFileSync(path.join(root, "scripts", file), "utf8");
    for (const match of text.matchAll(/runWorker\(\s*["'`]([a-z0-9_]+)["'`]/g)) {
      commands.add(match[1]);
    }
    for (const match of text.matchAll(/runWorker\(\{\s*command:\s*["'`]([a-z0-9_]+)["'`]/g)) {
      commands.add(match[1]);
    }
  }
  return [...commands].sort();
}

const registry = parseRegistryEntries();
const catalog = new Map(
  TOOL_SPECS.map((spec) => [spec.implementation.command, spec]),
);
const generated = new Set(WORKER_COMMANDS);
const errors = [];

for (const entry of registry) {
  const spec = catalog.get(entry.command);
  if (!spec) {
    errors.push(`WorkerCommandRegistry command missing from catalog: ${entry.command}`);
    continue;
  }
  const expectedHandler = spec.implementation.csharpHandler.replace(/^Program\./, "");
  if (entry.handler !== expectedHandler) {
    errors.push(
      `handler drift for ${entry.command}: catalog=${spec.implementation.csharpHandler}, registry=Program.${entry.handler}`,
    );
  }
}

for (const [command, spec] of catalog) {
  if (!registry.some((entry) => entry.command === command)) {
    errors.push(`catalog command missing from WorkerCommandRegistry: ${command}`);
  }
  if (!generated.has(command)) {
    errors.push(`catalog command missing from generated WorkerCommand union: ${command}`);
  }
  if (spec.implementation.kind !== "worker") {
    errors.push(`catalog command is not a worker implementation: ${command}`);
  }
}

for (const command of generated) {
  if (!catalog.has(command)) {
    errors.push(`generated WorkerCommand has no catalog entry: ${command}`);
  }
}

const registrySet = new Set(registry.map(({ command }) => command));
for (const command of parseScriptCommands()) {
  if (!registrySet.has(command)) {
    errors.push(`script references command missing from WorkerCommandRegistry: ${command}`);
  }
}

if (errors.length) {
  console.error("Registry consistency check failed:\n");
  for (const error of errors) {
    console.error(`- ${error}`);
  }
  process.exit(1);
}

console.log(
  JSON.stringify(
    {
      ok: true,
      registryCount: registry.length,
      catalogCount: catalog.size,
      generatedCount: generated.size,
      scriptCommandCount: parseScriptCommands().length,
    },
    null,
    2,
  ),
);
