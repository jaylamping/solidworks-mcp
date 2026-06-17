#!/usr/bin/env node
import fs from "node:fs";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const outPath = path.join(root, "generated/error-catalog.json");
const swconst = "C:/Program Files/SOLIDWORKS Corp/SOLIDWORKS/api/redist/SolidWorks.Interop.swconst.dll";

if (!fs.existsSync(swconst)) {
  const stub = { stub: true, message: "SolidWorks.Interop.swconst.dll not found", enums: {} };
  fs.mkdirSync(path.dirname(outPath), { recursive: true });
  fs.writeFileSync(outPath, `${JSON.stringify(stub, null, 2)}\n`);
  console.warn("swconst.dll missing — wrote stub error catalog");
  process.exit(0);
}

const script = `
$asm = [Reflection.Assembly]::LoadFrom('${swconst.replace(/\\/g, "\\\\")}')
$enums = @{}
foreach ($type in $asm.GetExportedTypes()) {
  if (-not $type.IsEnum) { continue }
  if ($type.Name -notmatch 'Error|e$') { continue }
  $values = @{}
  foreach ($name in [Enum]::GetNames($type)) {
    $values[$name] = [int][Enum]::Parse($type, $name)
  }
  $enums[$type.Name] = $values
}
$enums | ConvertTo-Json -Depth 5
`;

const result = spawnSync("powershell", ["-NoProfile", "-Command", script], { encoding: "utf8" });
if (result.status !== 0) {
  console.error(result.stderr || result.stdout);
  process.exit(1);
}

fs.mkdirSync(path.dirname(outPath), { recursive: true });
fs.writeFileSync(outPath, `${result.stdout.trim()}\n`);
console.log(`Wrote ${outPath}`);
