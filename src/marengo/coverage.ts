import fs from "node:fs/promises";
import path from "node:path";

import { runWorker } from "../worker.js";
import { loadVendorAssets } from "./manifests.js";
import { marengoRoot } from "./root.js";
import type { AuditFinding, AuditLevel } from "./cad-audit.js";

function finding(level: AuditLevel, code: string, message: string, filePath?: string): AuditFinding {
  return { level, code, message, ...(filePath ? { path: filePath } : {}) };
}

function summarize(findings: AuditFinding[]) {
  const fail = findings.filter((f) => f.level === "fail").length;
  const warn = findings.filter((f) => f.level === "warn").length;
  return { pass: findings.filter((f) => f.level === "pass").length, warn, fail, ok: fail === 0 };
}

interface BomRow {
  partId: string;
  qty: number;
  description: string;
}

/** Leaf SW instance name (strip sub-asm path prefix). */
function leafInstanceName(name: string): string {
  const slash = name.lastIndexOf("/");
  return slash >= 0 ? name.slice(slash + 1) : name;
}

function dedupeInstanceNames(names: string[]): string[] {
  const seen = new Set<string>();
  const unique: string[] = [];
  for (const name of names) {
    const leaf = leafInstanceName(name).toLowerCase();
    if (seen.has(leaf)) {
      continue;
    }
    seen.add(leaf);
    unique.push(name);
  }
  return unique;
}

function matchesPartId(instanceName: string, partId: string): boolean {
  const leaf = leafInstanceName(instanceName).toLowerCase();
  const id = partId.toLowerCase();

  if (leaf.includes(id)) {
    return true;
  }

  // frame_2020_vertical_340 → frame_2020_vertical_{back|front}_{left|right}_340
  if (id === "frame_2020_vertical_340") {
    return /^frame_2020_vertical_(?:back|front)_(?:left|right)_340(?:-\d+)?$/.test(leaf);
  }

  return false;
}

function countMatchingInstances(instanceNames: string[], partId: string): number {
  return instanceNames.filter((name) => matchesPartId(name, partId)).length;
}

export async function hardwareCoverage(args: { path?: string }): Promise<unknown> {
  const root = await marengoRoot();
  const vendor = await loadVendorAssets();
  const bomRows = await loadMasterBom(path.join(root, "hardware/bom/master-bom.csv"));
  const findings: AuditFinding[] = [];

  const bom = (await runWorker({
    command: "list_bom",
    args: { path: args.path },
  })) as { lines?: Array<{ name?: string | null; path?: string | null }> };

  const bomNames = dedupeInstanceNames(
    (bom.lines ?? []).map((line) => (line.name ?? "").toLowerCase()).filter(Boolean),
  );
  const bomPaths = (bom.lines ?? []).map((line) => (line.path ?? "").toLowerCase()).filter(Boolean);

  for (const asset of vendor.assets) {
    const tokens = [asset.id, asset.partNumber, asset.manufacturer].map((t) => t.toLowerCase());
    const inTree =
      tokens.some((token) => bomNames.some((name) => name.includes(token))) ||
      tokens.some((token) => bomPaths.some((p) => p.includes(token.replace(/\\/g, "/"))));

    const stepPath = asset.cad.stepPath ? path.join(root, asset.cad.stepPath) : null;
    let stepExists = false;
    if (stepPath) {
      try {
        await fs.access(stepPath);
        stepExists = true;
      } catch {
        stepExists = false;
      }
    }

    if (!inTree) {
      findings.push(
        finding("warn", "vendor_not_in_assembly", `Vendor asset not found in open assembly BOM: ${asset.id}`),
      );
    }
    if (stepPath && !stepExists) {
      findings.push(finding("fail", "vendor_step_missing", `Missing vendor STEP for ${asset.id}: ${stepPath}`));
    }
    if (inTree && (stepExists || !asset.cad.stepPath)) {
      findings.push(finding("pass", "vendor_covered", `Vendor asset present in tree and on disk: ${asset.id}`));
    }
  }

  for (const row of bomRows) {
    if (!row.partId) {
      continue;
    }
    const asset = vendor.assets.find((a) => a.id === row.partId);
    if (!asset && row.partId.startsWith("stock_")) {
      continue;
    }

    const instances = countMatchingInstances(bomNames, row.partId);
    if (!asset) {
      if (instances < row.qty) {
        findings.push(
          finding(
            "fail",
            "bom_qty_short",
            `Assembly has ${instances} instance(s) matching ${row.partId}, BOM expects ${row.qty}`,
          ),
        );
      } else if (instances > row.qty) {
        findings.push(
          finding(
            "warn",
            "bom_qty_high",
            `Assembly has ${instances} instance(s) matching ${row.partId}, BOM expects ${row.qty}`,
          ),
        );
      } else if (row.qty > 0) {
        findings.push(finding("pass", "bom_qty_ok", `BOM quantity satisfied for ${row.partId}`));
      }
      continue;
    }
    if (instances < row.qty) {
      findings.push(
        finding(
          "fail",
          "bom_qty_short",
          `Assembly has ${instances} instance(s) of ${row.partId}, BOM expects ${row.qty}`,
        ),
      );
    } else if (instances > row.qty) {
      findings.push(
        finding(
          "warn",
          "bom_qty_high",
          `Assembly has ${instances} instance(s) of ${row.partId}, BOM expects ${row.qty}`,
        ),
      );
    } else if (row.qty > 0) {
      findings.push(finding("pass", "bom_qty_ok", `BOM quantity satisfied for ${row.partId}`));
    }
  }

  if (vendor.assets.length === 0 && bomRows.length === 0) {
    findings.push(
      finding("warn", "coverage_unconfigured", "vendor-assets.json and master-bom.csv have no entries yet"),
    );
  }

  return {
    root,
    vendorAssetCount: vendor.assets.length,
    bomRowCount: bomRows.length,
    assemblyLineCount: bom.lines?.length ?? 0,
    findings,
    summary: summarize(findings),
  };
}

async function loadMasterBom(filePath: string): Promise<BomRow[]> {
  let raw: string;
  try {
    raw = await fs.readFile(filePath, "utf8");
  } catch {
    return [];
  }

  const lines = raw.split("\n").map((line) => line.trim()).filter(Boolean);
  if (lines.length <= 1) {
    return [];
  }

  const rows: BomRow[] = [];
  for (const line of lines.slice(1)) {
    const cols = line.split(",").map((c) => c.trim());
    if (cols.length < 3) {
      continue;
    }
    rows.push({
      partId: cols[0] ?? "",
      description: cols[1] ?? "",
      qty: Number.parseInt(cols[2] ?? "0", 10) || 0,
    });
  }
  return rows;
}
