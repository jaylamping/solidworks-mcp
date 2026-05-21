import { createHash } from "node:crypto";
import fs from "node:fs/promises";
import path from "node:path";

import { assertAllowedPath } from "./config.js";

const DEFAULT_REGISTRY = "C:/code/marengo/hardware/manifests/vendor-assets.json";

interface VendorRegistry {
  assets: VendorAsset[];
}

interface VendorAsset {
  id: string;
  manufacturer: string;
  supplier?: string;
  partNumber: string;
  category: string;
  source: {
    type: string;
    path?: string | null;
    url?: string | null;
    sha256?: string | null;
  };
  cad: {
    nativePath?: string | null;
    stepPath?: string | null;
    units: string;
  };
  status: string;
  notes?: string;
}

export function defaultRegistryPath(): string {
  return DEFAULT_REGISTRY;
}

export async function registrySummary(registryPath = DEFAULT_REGISTRY): Promise<unknown> {
  const registry = await loadRegistry(registryPath);
  const byStatus = countBy(registry.assets, (asset) => asset.status);
  const byCategory = countBy(registry.assets, (asset) => asset.category);

  return {
    registryPath: assertAllowedPath(registryPath),
    total: registry.assets.length,
    byStatus,
    byCategory,
    assets: registry.assets.map((asset) => ({
      id: asset.id,
      manufacturer: asset.manufacturer,
      partNumber: asset.partNumber,
      category: asset.category,
      status: asset.status,
      sourceType: asset.source.type,
      hasSha256: Boolean(asset.source.sha256),
      stepPath: asset.cad.stepPath,
      nativePath: asset.cad.nativePath,
    })),
  };
}

export async function stageLocalVendorAsset(args: {
  registryPath?: string;
  assetId: string;
  overwrite?: boolean;
  dryRun?: boolean;
}): Promise<unknown> {
  const registryPath = assertAllowedPath(args.registryPath ?? DEFAULT_REGISTRY);
  const vaultRoot = findVaultRoot(registryPath);
  const registry = await loadRegistry(registryPath);
  const asset = registry.assets.find((candidate) => candidate.id === args.assetId);
  if (!asset) {
    throw new Error(`Unknown vendor asset id: ${args.assetId}`);
  }
  if (asset.source.type !== "local" || !asset.source.path) {
    throw new Error(`Asset ${asset.id} does not have a local source path.`);
  }
  if (!asset.cad.stepPath) {
    throw new Error(`Asset ${asset.id} does not define cad.stepPath.`);
  }

  const sourcePath = assertAllowedPath(asset.source.path);
  const targetPath = assertAllowedPath(path.resolve(vaultRoot, asset.cad.stepPath));
  const sourceSha256 = await sha256File(sourcePath);

  const exists = await fileExists(targetPath);
  if (exists && !args.overwrite) {
    return {
      staged: false,
      reason: "target_exists",
      assetId: asset.id,
      sourcePath,
      targetPath,
      sha256: sourceSha256,
    };
  }

  if (!args.dryRun) {
    await fs.mkdir(path.dirname(targetPath), { recursive: true });
    await fs.copyFile(sourcePath, targetPath);
    asset.source.sha256 = sourceSha256;
    await fs.writeFile(`${registryPath}.tmp`, `${JSON.stringify(registry, null, 2)}\n`);
    await fs.rename(`${registryPath}.tmp`, registryPath);
  }

  return {
    staged: !args.dryRun,
    dryRun: Boolean(args.dryRun),
    assetId: asset.id,
    sourcePath,
    targetPath,
    sha256: sourceSha256,
  };
}

async function loadRegistry(registryPath: string): Promise<VendorRegistry> {
  const resolved = assertAllowedPath(registryPath);
  const raw = await fs.readFile(resolved, "utf8");
  return JSON.parse(raw) as VendorRegistry;
}

function findVaultRoot(registryPath: string): string {
  const normalized = path.resolve(registryPath);
  const hardwareManifests = `${path.sep}hardware${path.sep}manifests${path.sep}`;
  const hardwareIndex = normalized.toLowerCase().lastIndexOf(hardwareManifests);
  if (hardwareIndex >= 0) {
    return normalized.slice(0, hardwareIndex);
  }

  const marker = `${path.sep}manifests${path.sep}`;
  const index = normalized.toLowerCase().lastIndexOf(marker);
  if (index < 0) {
    return path.dirname(path.dirname(normalized));
  }
  return normalized.slice(0, index);
}

async function sha256File(filePath: string): Promise<string> {
  const hash = createHash("sha256");
  const data = await fs.readFile(filePath);
  hash.update(data);
  return hash.digest("hex");
}

async function fileExists(filePath: string): Promise<boolean> {
  try {
    await fs.stat(filePath);
    return true;
  } catch {
    return false;
  }
}

function countBy<T>(items: T[], keyFn: (item: T) => string): Record<string, number> {
  return items.reduce<Record<string, number>>((acc, item) => {
    const key = keyFn(item);
    acc[key] = (acc[key] ?? 0) + 1;
    return acc;
  }, {});
}
