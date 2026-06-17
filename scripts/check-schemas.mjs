#!/usr/bin/env node
const modules = [
  "../dist/schemas/mate.js",
  "../dist/schemas/assembly.js",
  "../dist/schemas/document.js",
];

for (const mod of modules) {
  await import(mod);
}

console.log(JSON.stringify({ ok: true, loaded: modules.length }, null, 2));
