import { appendFileSync, existsSync, mkdirSync, readFileSync } from "node:fs";
import path from "node:path";

import { allowedRoots } from "./config.js";

function auditLogPath(): string {
  const root = allowedRoots()[0] ?? process.cwd();
  const dir = path.join(root, "hardware", "cad");
  if (!existsSync(dir)) {
    mkdirSync(dir, { recursive: true });
  }
  return path.join(dir, ".mcp-audit.jsonl");
}

export function appendAuditEntry(entry: {
  tool: string;
  command: string;
  ok: boolean;
  destructive?: boolean;
  path?: string;
  error?: string;
}): void {
  const line = JSON.stringify({
    ...entry,
    timestamp: new Date().toISOString(),
  });
  try {
    appendFileSync(auditLogPath(), `${line}\n`, "utf8");
  } catch {
    // Audit log is best-effort; never break MCP stdio.
  }
}

export function readRecentAuditEntries(limit = 20): unknown[] {
  const file = auditLogPath();
  if (!existsSync(file)) {
    return [];
  }
  const lines = readFileSync(file, "utf8").trim().split("\n").filter(Boolean);
  return lines
    .slice(-limit)
    .map((line) => {
      try {
        return JSON.parse(line) as unknown;
      } catch {
        return { raw: line };
      }
    })
    .reverse();
}
