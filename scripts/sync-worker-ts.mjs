#!/usr/bin/env node
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const registryPath = path.join(root, "workers/SolidWorksComWorker/WorkerCommandRegistry.cs");
const workerTsPath = path.join(root, "src/worker.ts");

const text = fs.readFileSync(registryPath, "utf8");
const commands = [...new Set([...text.matchAll(/\["([a-z0-9_]+)"\]\s*=/g)].map((m) => m[1]))].sort();

const union = commands.map((c) => `  | "${c}"`).join("\n");
const workerTs = fs.readFileSync(workerTsPath, "utf8");
const updated = workerTs.replace(
  /export type WorkerCommand =[\s\S]*?;/,
  `export type WorkerCommand =\n${union};`,
);

fs.writeFileSync(workerTsPath, updated);
console.log(`Synced ${commands.length} commands to src/worker.ts`);
