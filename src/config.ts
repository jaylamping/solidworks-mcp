import path from "node:path";
import { fileURLToPath } from "node:url";

/** solidworks-mcp package root (works regardless of MCP process cwd). */
export function packageRoot(): string {
  return path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
}

export function workerProjectPath(): string {
  return path.join(packageRoot(), "workers/SolidWorksComWorker/SolidWorksComWorker.csproj");
}

/** Prebuilt worker DLL — use `dotnet exec` instead of `dotnet run` to avoid MSBuild races. */
export function workerDllPath(): string {
  return path.join(
    packageRoot(),
    "workers/SolidWorksComWorker/bin/Debug/net8.0-windows/SolidWorksComWorker.dll",
  );
}

/**
 * Normalize CAD paths without mangling UNC / WSL paths.
 * `path.resolve("\\wsl$\\...")` becomes `C:\\wsl$\\...` on Windows — never do that.
 */
export function normalizeCadPath(inputPath: string): string {
  const trimmed = inputPath.trim();
  if (!trimmed) {
    return trimmed;
  }

  const unified = trimmed.replace(/\//g, "\\");

  // UNC: \\server\share\...
  if (unified.startsWith("\\\\")) {
    return path.win32.normalize(unified);
  }

  // Drive-absolute: C:\... or C:/...
  if (/^[a-zA-Z]:[\\/]/.test(trimmed)) {
    return path.win32.normalize(unified);
  }

  return path.resolve(trimmed);
}

function pathUnderRoot(candidate: string, root: string): boolean {
  const normalized = normalizeCadPath(candidate).toLowerCase();
  const normalizedRoot = normalizeCadPath(root).toLowerCase();
  if (normalized === normalizedRoot) {
    return true;
  }
  const prefix = normalizedRoot.endsWith("\\") ? normalizedRoot : `${normalizedRoot}\\`;
  return normalized.startsWith(prefix);
}

export function allowedRoots(): string[] {
  const raw = process.env.SOLIDWORKS_MCP_ALLOWED_ROOTS;
  const roots = raw
    ? raw.split(";").map((entry) => entry.trim()).filter(Boolean)
    : [];

  return roots.map((root) => normalizeCadPath(root));
}

export function assertAllowedPath(inputPath: string): string {
  const resolved = normalizeCadPath(inputPath);
  const roots = allowedRoots();
  const allowed = roots.some((root) => pathUnderRoot(resolved, root));

  if (!allowed) {
    throw new Error(
      `Path is outside allowed CAD roots: ${resolved}. Set SOLIDWORKS_MCP_ALLOWED_ROOTS to allow it.`,
    );
  }

  return resolved;
}

/**
 * Document lookup paths: normalize only. The worker trusts already-open docs and
 * still PathGuard-checks opens from disk (README: "Open docs are trusted even if omitted").
 */
export function prepareDocumentPath(inputPath: string): string {
  return normalizeCadPath(inputPath);
}
