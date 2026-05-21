import path from "node:path";
import { fileURLToPath } from "node:url";

const DEFAULT_ALLOWED_ROOTS = ["C:/code/marengo"];

/** solidworks-mcp package root (works regardless of MCP process cwd). */
export function packageRoot(): string {
  return path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
}

export function workerProjectPath(): string {
  return path.join(packageRoot(), "workers/SolidWorksComWorker/SolidWorksComWorker.csproj");
}

export function allowedRoots(): string[] {
  const raw = process.env.SOLIDWORKS_MCP_ALLOWED_ROOTS;
  const roots = raw
    ? raw.split(";").map((entry) => entry.trim()).filter(Boolean)
    : DEFAULT_ALLOWED_ROOTS;

  return roots.map((root) => path.resolve(root));
}

export function assertAllowedPath(inputPath: string): string {
  const resolved = path.resolve(inputPath);
  const normalized = resolved.toLowerCase();
  const allowed = allowedRoots().some((root) => {
    const normalizedRoot = path.resolve(root).toLowerCase();
    return normalized === normalizedRoot || normalized.startsWith(`${normalizedRoot}${path.sep}`);
  });

  if (!allowed) {
    throw new Error(
      `Path is outside allowed CAD roots: ${resolved}. Set SOLIDWORKS_MCP_ALLOWED_ROOTS to allow it.`,
    );
  }

  return resolved;
}
