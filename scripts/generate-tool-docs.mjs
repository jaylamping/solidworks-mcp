#!/usr/bin/env node
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const manifestPath = path.join(root, "tools/manifest.json");
const outDir = path.join(root, "docs/api");

if (!fs.existsSync(manifestPath)) {
  console.error("tools/manifest.json missing — run scripts/generate-manifest-from-registry.mjs first");
  process.exit(1);
}

const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
fs.mkdirSync(outDir, { recursive: true });

for (const tool of manifest.tools ?? []) {
  const file = path.join(outDir, `${tool.name}.md`);
  const body = `# ${tool.name}

${tool.description}

| Field | Value |
|-------|-------|
| Worker command | \`${tool.workerCommand}\` |
| Tier | ${tool.tier} |
| Read only | ${tool.readOnly} |
| Destructive | ${tool.destructive ?? false} |
| Confirm required | ${tool.confirmRequired ?? false} |

## Tags

${(tool.tags ?? []).map((t) => `- ${t}`).join("\n") || "- none"}

## Domains

${(tool.domains ?? []).map((d) => `- ${d}`).join("\n") || "- none"}
`;
  fs.writeFileSync(file, `${body}\n`);
}

console.log(`Wrote ${manifest.tools?.length ?? 0} tool docs to ${outDir}`);
