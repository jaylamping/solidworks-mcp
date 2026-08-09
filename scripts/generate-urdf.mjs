#!/usr/bin/env node
import path from "node:path";
import { pathToFileURL } from "node:url";

const packagePath = process.argv[2];
const urdfOutputPath = process.argv[3];
const meshUriPrefix = process.argv[4];

if (!packagePath) {
  console.error("Usage: node scripts/generate-urdf.mjs <packageDir> [urdfOut] [meshUriPrefix]");
  process.exit(1);
}

const { writeUrdfFromPackage } = await import(
  pathToFileURL(path.resolve("src/urdf/generate.ts")).href
);

const result = writeUrdfFromPackage({
  packagePath: path.resolve(packagePath),
  urdfOutputPath: urdfOutputPath ? path.resolve(urdfOutputPath) : undefined,
  meshUriPrefix,
});
console.log(JSON.stringify(result, null, 2));
