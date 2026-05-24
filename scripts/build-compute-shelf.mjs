/**
 * Rebuild marengo_torso_compute_shelf_upper_revA from live shoulder actuator geometry.
 * Requires SolidWorks running with marengo_torso_asm_revA open or on disk.
 *
 * Usage:
 *   node scripts/build-compute-shelf.mjs
 */
import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const workerProject = path.join(root, "workers/SolidWorksComWorker");

const ASM =
  "C:/code/marengo/hardware/cad/assemblies/marengo_torso_asm_revA.SLDASM";
const PART =
  "C:/code/marengo/hardware/cad/parts/marengo_torso_compute_shelf_upper_revA.SLDPRT";

function runWorker(command, args) {
  const payload = JSON.stringify({ command, args });
  const result = spawnSync(
    "dotnet",
    ["run", "--project", workerProject, "--no-launch-profile", "-v", "q"],
    { cwd: root, input: payload, encoding: "utf8", windowsHide: true },
  );
  const stdout = (result.stdout || "").trim();
  const stderr = (result.stderr || "").trim();
  const jsonLine = stdout.split("\n").find((line) => line.startsWith("{"));
  if (!jsonLine) {
    throw new Error(stderr || stdout || `worker exit ${result.status}`);
  }
  const parsed = JSON.parse(jsonLine);
  if (!parsed.ok) {
    throw new Error(parsed.error || "worker failed");
  }
  return parsed.data;
}

const result = runWorker("build_torso_compute_shelf", {
  path: ASM,
  part_path: PART,
  save: true,
  mate_in_assembly: true,
});

console.log(JSON.stringify(result, null, 2));
