#!/usr/bin/env node
/**
 * Stub scraper for SolidWorks 2026 API Help.
 * Writes cache metadata; full crawl can be resumed later.
 */
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const cacheDir = path.join(root, "generated/help-cache");
const indexPath = path.join(cacheDir, "index.json");

fs.mkdirSync(cacheDir, { recursive: true });
const index = {
  stub: true,
  baseUrl: "https://help.solidworks.com/2026/english/api/sldworksapiprogguide/",
  message: "Help scrape not run — use interop reflection catalog as primary source.",
  cachedPages: 0,
  updatedAt: new Date().toISOString(),
};
fs.writeFileSync(indexPath, `${JSON.stringify(index, null, 2)}\n`);
console.log(`Wrote help cache stub to ${indexPath}`);
