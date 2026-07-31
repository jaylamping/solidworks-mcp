#!/usr/bin/env node
/**
 * Clean-env MCP handshake: initialize + tools/list.
 * Asserts non-empty tools and zero product tool names.
 */
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const productName =
  /\b(marengo_|solidworks_build_torso|solidworks_actuator_|solidworks_layout_add_shoulder|torso_|shoulder_|mallet|robstride|rs03)\b/i;

const transport = new StdioClientTransport({
  command: "node",
  args: [
    path.join(root, "node_modules/tsx/dist/cli.mjs"),
    path.join(root, "src/index.ts"),
  ],
  env: {
    ...process.env,
    SOLIDWORKS_MCP_TOOL_TIER: "extended",
  },
  stderr: "pipe",
});

const client = new Client({ name: "solidworks-mcp-handshake", version: "0.0.1" });
await client.connect(transport);

const tools = [];
let cursor;
do {
  const page = await client.listTools(cursor ? { cursor } : undefined);
  tools.push(...(page.tools ?? []));
  cursor = page.nextCursor;
} while (cursor);

const names = tools.map((t) => t.name).sort();
const productHits = names.filter((name) => productName.test(name));

const report = {
  ok: names.length > 0 && productHits.length === 0,
  toolCount: names.length,
  productHits,
  sample: names.slice(0, 20),
  hasUrdfReadiness: names.includes("solidworks_urdf_readiness"),
  hasAddUrdfFrame: names.includes("solidworks_add_urdf_frame"),
  hasListRefGeom: names.includes("solidworks_list_reference_geometry"),
  hasListMates: names.includes("solidworks_list_mates"),
  hasStatus: names.includes("solidworks_status"),
};

console.log(JSON.stringify(report, null, 2));
await client.close();
process.exit(report.ok ? 0 : 1);
