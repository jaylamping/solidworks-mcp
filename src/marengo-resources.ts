import { readFileSync } from "node:fs";

import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";

import { marengoPath, resolveMarengoFile } from "./marengo/root.js";

const MANIFEST_FILES = [
  { uri: "marengo://manifests/cad-conventions", file: "cad/manifests/cad-conventions.json" },
  { uri: "marengo://manifests/vendor-assets", file: "cad/manifests/vendor-assets.json" },
  { uri: "marengo://manifests/design-packages", file: "cad/manifests/design-packages.json" },
  { uri: "marengo://docs/kinematics", file: "hardware/docs/kinematics.md" },
  { uri: "marengo://bom/master", file: "hardware/bom/master-bom.csv" },
] as const;

export function registerMarengoResources(server: McpServer): void {
  for (const entry of MANIFEST_FILES) {
    server.registerResource(
      entry.uri.replace("marengo://", "marengo-").replace(/\//g, "-"),
      entry.uri,
      {
        title: entry.uri,
        description: `Marengo manifest: ${entry.file}`,
        mimeType: entry.file.endsWith(".md") ? "text/markdown" : "application/json",
      },
      async () => {
        try {
          const resolved = await resolveMarengoFile(entry.file);
          const text = readFileSync(resolved, "utf8");
          return {
            contents: [
              {
                uri: entry.uri,
                mimeType: entry.file.endsWith(".md") ? "text/markdown" : "application/json",
                text,
              },
            ],
          };
        } catch {
          return {
            contents: [
              {
                uri: entry.uri,
                mimeType: "text/plain",
                text: `Manifest not found under Marengo root. Set MARENGO_ROOT or SOLIDWORKS_MCP_ALLOWED_ROOTS. Expected: ${entry.file}`,
              },
            ],
          };
        }
      },
    );
  }

  server.registerResource(
    "marengo-root",
    "marengo://root",
    {
      title: "Marengo project root",
      description: "Resolved Marengo repository root path.",
      mimeType: "text/plain",
    },
    async () => {
      const root = await marengoPath();
      return { contents: [{ uri: "marengo://root", mimeType: "text/plain", text: root }] };
    },
  );
}

export function marengoResourceIndex(): { uri: string; file: string }[] {
  return MANIFEST_FILES.map((e) => ({ uri: e.uri, file: e.file }));
}
