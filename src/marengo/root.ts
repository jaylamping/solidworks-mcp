import fs from "node:fs/promises";
import path from "node:path";

import { allowedRoots, assertAllowedPath } from "../config.js";

const MANIFEST_MARKER = path.join("hardware", "manifests", "vendor-assets.json");

export async function marengoRoot(): Promise<string> {
  const fromEnv = process.env.MARENGO_ROOT?.trim();
  if (fromEnv) {
    return assertAllowedPath(fromEnv);
  }

  for (const root of allowedRoots()) {
    const marker = path.join(root, MANIFEST_MARKER);
    try {
      await fs.access(marker);
      return root;
    } catch {
      // try next root
    }
  }

  const fallback = allowedRoots()[0];
  if (!fallback) {
    throw new Error("No allowed CAD roots configured. Set SOLIDWORKS_MCP_ALLOWED_ROOTS.");
  }
  return fallback;
}

export function marengoPath(...segments: string[]): Promise<string> {
  return marengoRoot().then((root) => path.join(root, ...segments));
}

export async function resolveMarengoFile(relativePath: string): Promise<string> {
  const root = await marengoRoot();
  return assertAllowedPath(path.join(root, relativePath));
}
