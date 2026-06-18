import fs from "node:fs/promises";
import path from "node:path";

import { assertAllowedPath } from "../config.js";
import { runWorker } from "../worker.js";
import {
  loadCadConventions,
  loadDesignPackages,
  matchesPattern,
} from "./manifests.js";
import { marengoRoot, resolveMarengoFile } from "./root.js";

export type AuditLevel = "pass" | "warn" | "fail";

export interface AuditFinding {
  level: AuditLevel;
  code: string;
  message: string;
  path?: string;
}

function finding(level: AuditLevel, code: string, message: string, filePath?: string): AuditFinding {
  return { level, code, message, ...(filePath ? { path: filePath } : {}) };
}

export async function cadConventionsCheck(args: {
  path?: string;
  inspectCustomProperties?: boolean;
}): Promise<unknown> {
  const root = await marengoRoot();
  const conventions = await loadCadConventions();
  const cadRoot = path.join(root, conventions.cadRoot);
  const target = args.path ? assertAllowedPath(args.path) : cadRoot;
  const findings: AuditFinding[] = [];

  if (!target.toLowerCase().startsWith(cadRoot.toLowerCase())) {
    findings.push(
      finding("fail", "outside_cad_root", `Path is outside ${conventions.cadRoot}: ${target}`, target),
    );
    return { root, target, findings, summary: summarize(findings) };
  }

  const files = await resolveCadTargets(target);
  for (const file of files) {
    const base = path.basename(file);
    const ext = path.extname(base).toLowerCase();
    if (ext === ".sldprt") {
      if (!matchesPattern(base, conventions.partFilenamePattern)) {
        findings.push(
          finding("fail", "part_name", `Part filename does not match convention: ${base}`, file),
        );
      }
    } else if (ext === ".sldasm") {
      if (!matchesPattern(base, conventions.assemblyFilenamePattern)) {
        findings.push(
          finding("fail", "assembly_name", `Assembly filename does not match convention: ${base}`, file),
        );
      }
    } else if (ext === ".step" || ext === ".stp") {
      if (!matchesPattern(base, conventions.vendorFilenamePattern)) {
        findings.push(finding("warn", "vendor_name", `Vendor STEP name may be non-standard: ${base}`, file));
      }
    }
  }

  if (args.inspectCustomProperties) {
    const partPaths = await resolveInspectablePartPaths(target, files);
    for (const partPath of partPaths) {
      const inspected = await runWorker({
        command: "inspect_document",
        args: { path: partPath },
      });
      const props = extractCustomPropertyNames(inspected);
      for (const required of conventions.requiredCustomProperties) {
        if (!props.has(required.toLowerCase())) {
          findings.push(
            finding(
              "fail",
              "missing_custom_property",
              `Missing custom property '${required}' on ${path.basename(partPath)}`,
              partPath,
            ),
          );
        } else {
          findings.push(
            finding(
              "pass",
              "custom_property",
              `Custom property present on ${path.basename(partPath)}: ${required}`,
              partPath,
            ),
          );
        }
      }
    }
  }

  return { root, target, fileCount: files.length, findings, summary: summarize(findings) };
}

export async function designPackageValidate(args: {
  path?: string;
  packageId?: string;
}): Promise<unknown> {
  const root = await marengoRoot();
  const manifest = await loadDesignPackages();
  const pkg =
    manifest.packages.find((entry) => entry.id === (args.packageId ?? "marengo")) ??
    manifest.packages[0];
  if (!pkg) {
    throw new Error("No design package defined in design-packages.json");
  }

  const assemblyPath = await resolveMarengoFile(pkg.assemblyPath);
  const openPath = args.path ? assertAllowedPath(args.path) : assemblyPath;
  const findings: AuditFinding[] = [];

  if (!openPath.toLowerCase().endsWith(path.basename(assemblyPath).toLowerCase())) {
    findings.push(
      finding(
        "warn",
        "unexpected_assembly",
        `Validating ${openPath} but package expects ${pkg.assemblyPath}`,
        openPath,
      ),
    );
  }

  try {
    await fs.access(openPath);
  } catch {
    findings.push(finding("fail", "assembly_missing", `Assembly not found: ${openPath}`, openPath));
    return { package: pkg, findings, summary: summarize(findings) };
  }

  const components = (await runWorker({
    command: "list_components",
    args: { path: openPath },
  })) as { components?: Array<{ name?: string | null }> };

  const names = (components.components ?? [])
    .map((row) => (row.name ?? "").toLowerCase())
    .filter(Boolean);

  for (const pattern of pkg.requiredChildNamePatterns) {
    const regex = new RegExp(pattern, "i");
    if (!names.some((name) => regex.test(name))) {
      findings.push(
        finding("fail", "missing_child", `No component matches required pattern: ${pattern}`, openPath),
      );
    }
  }

  for (const assetId of pkg.requiredVendorAssetIds) {
    if (!names.some((name) => name.includes(assetId.toLowerCase()))) {
      findings.push(
        finding("fail", "missing_vendor_instance", `No component instance for vendor asset: ${assetId}`, openPath),
      );
    }
  }

  if (pkg.requiredChildNamePatterns.length === 0 && pkg.requiredVendorAssetIds.length === 0) {
    findings.push(
      finding(
        "warn",
        "package_unconfigured",
        "Design package has no requiredChildNamePatterns or requiredVendorAssetIds yet",
        openPath,
      ),
    );
  }

  return {
    root,
    package: pkg,
    assemblyPath: openPath,
    componentCount: names.length,
    findings,
    summary: summarize(findings),
  };
}

export async function designReview(args: {
  path?: string;
  packageId?: string;
}): Promise<unknown> {
  const conventions = await cadConventionsCheck({
    path: args.path,
    inspectCustomProperties: Boolean(args.path),
  });
  const packageResult = await designPackageValidate({ path: args.path, packageId: args.packageId });
  const checklist = [
    { item: "CAD under cad/", done: true },
    { item: "Filenames match cad-conventions.json", done: (conventions as { summary: { fail: number } }).summary.fail === 0 },
    { item: "Design package tree requirements", done: (packageResult as { summary: { fail: number } }).summary.fail === 0 },
    {
      item: "Required custom properties on assembly parts",
      done: (conventions as { findings: AuditFinding[] }).findings.every(
        (entry) => entry.code !== "missing_custom_property",
      ),
    },
    { item: "See hardware/docs/cad-standards.md for URDF ref names before export", done: false },
  ];

  const findings = [
    ...((conventions as { findings: AuditFinding[] }).findings ?? []),
    ...((packageResult as { findings: AuditFinding[] }).findings ?? []),
  ];

  return {
    conventions,
    package: packageResult,
    checklist,
    findings,
    summary: summarize(findings),
  };
}

function summarize(findings: AuditFinding[]): { pass: number; warn: number; fail: number; ok: boolean } {
  const pass = findings.filter((f) => f.level === "pass").length;
  const warn = findings.filter((f) => f.level === "warn").length;
  const fail = findings.filter((f) => f.level === "fail").length;
  return { pass, warn, fail, ok: fail === 0 };
}

function isSolidWorksLockFile(name: string): boolean {
  return name.startsWith("~$");
}

async function resolveCadTargets(target: string): Promise<string[]> {
  const stat = await fs.stat(target);
  if (stat.isFile()) {
    const ext = path.extname(target).toLowerCase();
    return [".sldprt", ".sldasm", ".step", ".stp"].includes(ext) ? [target] : [];
  }
  return listCadFiles(target);
}

async function resolveInspectablePartPaths(target: string, files: string[]): Promise<string[]> {
  const ext = path.extname(target).toLowerCase();
  if (ext === ".sldprt") {
    return [target];
  }

  if (ext === ".sldasm") {
    const components = (await runWorker({
      command: "list_components",
      args: { path: target },
    })) as { components?: Array<{ path?: string | null; suppressed?: boolean | null }> };

    const partPaths = new Set<string>();
    for (const component of components.components ?? []) {
      if (component.suppressed || !component.path) {
        continue;
      }
      if (path.extname(component.path).toLowerCase() === ".sldprt") {
        partPaths.add(component.path);
      }
    }
    return [...partPaths];
  }

  return files.filter((file) => path.extname(file).toLowerCase() === ".sldprt");
}

async function listCadFiles(dir: string): Promise<string[]> {
  const entries = await fs.readdir(dir, { withFileTypes: true });
  const files: string[] = [];
  for (const entry of entries) {
    if (isSolidWorksLockFile(entry.name)) {
      continue;
    }

    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      if (entry.name === "exports") {
        continue;
      }
      files.push(...(await listCadFiles(full)));
      continue;
    }
    const ext = path.extname(entry.name).toLowerCase();
    if ([".sldprt", ".sldasm", ".step", ".stp"].includes(ext)) {
      files.push(full);
    }
  }
  return files;
}

function extractCustomPropertyNames(inspected: unknown): Set<string> {
  const names = new Set<string>();
  const props = (inspected as { customProperties?: Array<{ name?: string }> })?.customProperties ?? [];
  for (const row of props) {
    if (row.name) {
      names.add(row.name.toLowerCase());
    }
  }
  return names;
}
