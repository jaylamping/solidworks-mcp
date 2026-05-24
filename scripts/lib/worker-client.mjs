import { spawnSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const packageRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const workerProject = path.join(packageRoot, "workers/SolidWorksComWorker");
const workerDll = path.join(
  packageRoot,
  "workers/SolidWorksComWorker/bin/Debug/net8.0-windows/SolidWorksComWorker.dll",
);

/**
 * @param {string} command
 * @param {Record<string, unknown>} [args]
 */
export function runWorker(command, args = {}) {
  const payload = JSON.stringify({ command, args });
  const useDll = fs.existsSync(workerDll);
  const dotnetArgs = useDll
    ? ["exec", workerDll]
    : ["run", "--project", workerProject, "--no-launch-profile", "-v", "q"];

  const result = spawnSync("dotnet", dotnetArgs, {
    cwd: packageRoot,
    input: payload,
    encoding: "utf8",
    windowsHide: true,
  });

  const output = (result.stdout || "").trim();
  if (result.status !== 0 && !output.includes('"ok"')) {
    throw new Error(result.stderr || output || `Worker exited ${result.status}`);
  }

  let parsed;
  try {
    parsed = JSON.parse(output);
  } catch {
    const start = output.indexOf("{");
    const end = output.lastIndexOf("}");
    if (start < 0 || end <= start) {
      throw new Error(result.stderr || output || "Worker returned non-JSON output");
    }
    parsed = JSON.parse(output.slice(start, end + 1));
  }

  if (!parsed.ok) {
    throw new Error(parsed.error || "Worker failed");
  }

  return parsed.data;
}

/**
 * @param {string[]} argv
 * @param {string} usage
 */
export function requireConfirm(argv, usage) {
  if (!argv.includes("--confirm")) {
    console.error(`Refusing to run: destructive SolidWorks script.\n  ${usage}`);
    process.exit(1);
  }
}

/**
 * @param {string} step
 * @param {unknown} result
 * @param {Array<{ step: string, result: unknown }>} steps
 */
export function logStep(step, result, steps) {
  const entry = { step, result };
  steps.push(entry);
  return entry;
}

export { packageRoot, workerProject };
