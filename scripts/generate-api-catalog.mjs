#!/usr/bin/env node
import fs from "node:fs";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const outPath = path.join(root, "generated/api-catalog.json");
const dll = "C:/Program Files/SOLIDWORKS Corp/SOLIDWORKS/api/redist/SolidWorks.Interop.sldworks.dll";

if (!fs.existsSync(dll)) {
  const stub = { stub: true, message: "SolidWorks.Interop.sldworks.dll not found", interfaces: [] };
  fs.mkdirSync(path.dirname(outPath), { recursive: true });
  fs.writeFileSync(outPath, `${JSON.stringify(stub, null, 2)}\n`);
  console.warn("sldworks.dll missing — wrote stub api catalog");
  process.exit(0);
}

const script = `
$asm = [Reflection.Assembly]::LoadFrom('${dll.replace(/\\/g, "\\\\")}')
$ifaces = @()
foreach ($type in $asm.GetExportedTypes()) {
  if (-not $type.IsInterface) { continue }
  if ($type.Name -notlike 'I*') { continue }
  $methods = @($type.GetMethods() | ForEach-Object { $_.Name } | Sort-Object -Unique)
  if ($methods.Count -eq 0) { continue }
  $ifaces += [ordered]@{ name = $type.Name; methods = $methods }
}
[ordered]@{ generatedAt = (Get-Date).ToString('o'); interfaceCount = $ifaces.Count; interfaces = $ifaces } | ConvertTo-Json -Depth 6
`;

const result = spawnSync("powershell", ["-NoProfile", "-Command", script], { encoding: "utf8", maxBuffer: 50 * 1024 * 1024 });
if (result.status !== 0) {
  console.error(result.stderr || result.stdout?.slice(0, 500));
  process.exit(1);
}

fs.mkdirSync(path.dirname(outPath), { recursive: true });
fs.writeFileSync(outPath, `${result.stdout.trim()}\n`);
console.log(`Wrote ${outPath}`);
