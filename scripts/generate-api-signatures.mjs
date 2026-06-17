#!/usr/bin/env node
/**
 * Generate invoke allowlist signatures from SolidWorks interop DLL (Windows only).
 * Run on machine with SolidWorks installed.
 */
import fs from "node:fs";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(__dirname, "..");
const OUT = path.join(ROOT, "docs/api-reference/generated");

const cs = `
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

var dll = @"C:\\Program Files\\SOLIDWORKS Corp\\SOLIDWORKS\\api\\redist\\SolidWorks.Interop.sldworks.dll";
if (!File.Exists(dll)) { Console.Error.WriteLine("Interop DLL not found: " + dll); Environment.Exit(2); }
var asm = Assembly.LoadFrom(dll);
var types = asm.GetExportedTypes().Where(t => t.IsInterface && t.Name.StartsWith("I")).Take(500);
var entries = types.SelectMany(t => t.GetMembers().Where(m => m.MemberType is MemberTypes.Method or MemberTypes.Property).Select(m => new {
  type = t.FullName,
  member = m.Name,
  kind = m.MemberType.ToString(),
  parameters = m is MethodInfo mi ? mi.GetParameters().Select(p => p.ParameterType.Name).ToArray() : Array.Empty<string>()
})).ToList();
Directory.CreateDirectory(args[0]);
File.WriteAllText(Path.Combine(args[0], "signatures.json"), JsonSerializer.Serialize(new { generatedAt = DateTime.UtcNow, count = entries.Count, entries }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("Wrote " + entries.Count + " signatures");
`;

const tmpDir = path.join(ROOT, "scripts", ".gen-sigs");
fs.mkdirSync(tmpDir, { recursive: true });
fs.writeFileSync(path.join(tmpDir, "Program.cs"), cs);
fs.writeFileSync(
  path.join(tmpDir, "gen-sigs.csproj"),
  `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>`,
);

const r = spawnSync("dotnet", ["run", "--project", tmpDir, "--", OUT], { encoding: "utf8", cwd: ROOT });
if (r.status !== 0) {
  console.warn("Signature generation skipped (Windows + SolidWorks required):", r.stderr || r.stdout);
  fs.mkdirSync(OUT, { recursive: true });
  if (!fs.existsSync(path.join(OUT, "signatures.json"))) {
    fs.writeFileSync(
      path.join(OUT, "signatures.json"),
      JSON.stringify({ generatedAt: new Date().toISOString(), count: 0, entries: [], note: "Run on Windows with SolidWorks installed" }, null, 2),
    );
  }
  process.exit(0);
}
console.log(r.stdout);
