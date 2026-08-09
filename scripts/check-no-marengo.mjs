#!/usr/bin/env node
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const denylist = [
  /\bmarengo\b/i,
  /C:[/\\]+code[/\\]+marengo/i,
  /\btorso_/i,
  /\bshoulder_/i,
  /\bmallet\b/i,
  /\brobstride\b/i,
  /\brs03\b/i,
  /\bactuator_/i,
  /\bsolidworks_build_torso\b/i,
  /\bsolidworks_actuator_/i,
  /\bsolidworks_layout_add_shoulder\b/i,
  /\bmarengo_/i,
];

const scanRoots = [
  "src",
  "scripts",
  "workers",
  "tools",
  "package.json",
  ".cursor/mcp.json",
];
const ignoredFiles = new Set([
  "scripts/check-no-marengo.mjs",
  // Intentional product-name detector; scanning its denylist regex is a false positive.
  "scripts/mcp-handshake.mjs",
]);
const textExtensions = new Set([".cs", ".json", ".mjs", ".ts", ".tsx", ".js", ".md", ".csproj"]);

function filesUnder(relativePath) {
  const absolutePath = path.join(root, relativePath);
  if (!fs.existsSync(absolutePath)) return [];
  if (fs.statSync(absolutePath).isFile()) return [absolutePath];

  const files = [];
  for (const entry of fs.readdirSync(absolutePath, { withFileTypes: true })) {
    const child = path.join(absolutePath, entry.name);
    if (entry.isDirectory()) files.push(...filesUnder(path.relative(root, child)));
    else if (textExtensions.has(path.extname(entry.name).toLowerCase())) files.push(child);
  }
  return files;
}

const findings = [];
for (const scanRoot of scanRoots) {
  for (const file of filesUnder(scanRoot)) {
    const relativeFile = path.relative(root, file).replaceAll(path.sep, "/");
    if (ignoredFiles.has(relativeFile)) continue;

    const lines = fs.readFileSync(file, "utf8").split(/\r?\n/);
    lines.forEach((line, index) => {
      if (relativeFile === "package.json" && line.includes("check-no-marengo")) return;
      // Allowed-root path lists are configuration, not product tooling leakage.
      if (line.includes("SOLIDWORKS_MCP_ALLOWED_ROOTS")) return;
      if (denylist.some((pattern) => pattern.test(line))) {
        findings.push(`${relativeFile}:${index + 1}: ${line.trim()}`);
      }
    });
  }
}

if (findings.length > 0) {
  console.error("Product separation gate failed:");
  for (const finding of findings) console.error(`- ${finding}`);
  process.exit(1);
}

console.log(`Product separation gate passed (${scanRoots.length} roots scanned).`);
