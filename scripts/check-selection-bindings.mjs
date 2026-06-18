#!/usr/bin/env node
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

import { SCHEMA_BY_COMMAND } from "./schema-by-command.mjs";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const selectionContextPath = path.join(
  root,
  "workers/SolidWorksComWorker/Interop/SelectionContext.cs",
);

function parseSelectionBindings(text) {
  const bindings = new Set();
  for (const match of text.matchAll(/\["([a-z0-9_]+)"\]\s*=/g)) {
    if (match.index !== undefined && text.slice(Math.max(0, match.index - 80), match.index).includes("SelectionCommandBindings")) {
      bindings.add(match[1]);
    }
  }
  const block = text.match(/SelectionCommandBindings\s*=\s*new[\s\S]*?};/);
  if (block) {
    for (const match of block[0].matchAll(/\["([a-z0-9_]+)"\]\s*=/g)) {
      bindings.add(match[1]);
    }
  }
  return [...bindings].sort();
}

const bindings = parseSelectionBindings(fs.readFileSync(selectionContextPath, "utf8"));
const schemaMap = new Map(Object.entries(SCHEMA_BY_COMMAND));

const missingSchema = bindings.filter((command) => !schemaMap.has(command));
if (missingSchema.length) {
  console.error("Selection-bound commands missing SCHEMA_BY_COMMAND entry:");
  for (const command of missingSchema) console.error(`- ${command}`);
  process.exit(1);
}

console.log(JSON.stringify({ ok: true, selectionBindingCount: bindings.length }, null, 2));
