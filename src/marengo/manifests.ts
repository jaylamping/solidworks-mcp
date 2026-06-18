import fs from "node:fs/promises";
import path from "node:path";

import { resolveMarengoFile } from "./root.js";

export interface CadConventions {
  version: number;
  cadRoot: string;
  partFilenamePattern: string;
  assemblyFilenamePattern: string;
  vendorFilenamePattern: string;
  requiredCustomProperties: string[];
  requiredUrdfReferenceNames: string[];
  recommendedReferenceNames: string[];
  hardwareInstanceFolder: string;
}

export interface DesignPackage {
  id: string;
  title: string;
  assemblyPath: string;
  requiredChildNamePatterns: string[];
  requiredVendorAssetIds: string[];
}

export interface DesignPackagesManifest {
  version: number;
  packages: DesignPackage[];
}

export interface VendorAsset {
  id: string;
  manufacturer: string;
  partNumber: string;
  category: string;
  status: string;
  cad: { stepPath?: string | null; nativePath?: string | null };
}

export interface VendorAssetsManifest {
  version: number;
  assets: VendorAsset[];
}

export async function loadCadConventions(): Promise<CadConventions> {
  return loadJson<CadConventions>("cad/manifests/cad-conventions.json");
}

export async function loadDesignPackages(): Promise<DesignPackagesManifest> {
  return loadJson<DesignPackagesManifest>("cad/manifests/design-packages.json");
}

export async function loadVendorAssets(): Promise<VendorAssetsManifest> {
  return loadJson<VendorAssetsManifest>("cad/manifests/vendor-assets.json");
}

async function loadJson<T>(relativePath: string): Promise<T> {
  const filePath = await resolveMarengoFile(relativePath);
  const raw = await fs.readFile(filePath, "utf8");
  return JSON.parse(raw) as T;
}

export function matchesPattern(filename: string, pattern: string): boolean {
  return new RegExp(pattern, "i").test(path.basename(filename));
}
