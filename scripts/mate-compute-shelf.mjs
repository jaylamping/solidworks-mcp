/** Place + mate compute shelf in torso asm (no part rebuild). */
import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const workerProject = path.join(root, "workers/SolidWorksComWorker");
const ASM =
  "C:/code/marengo/hardware/cad/assemblies/marengo_torso_asm_revA.SLDASM";

function runWorker(command, args) {
  const payload = JSON.stringify({ command, args });
  const result = spawnSync(
    "dotnet",
    ["run", "--project", workerProject, "--no-launch-profile", "-v", "q"],
    { cwd: root, input: payload, encoding: "utf8", windowsHide: true },
  );
  const jsonLine = (result.stdout || "").trim().split("\n").find((l) => l.startsWith("{"));
  if (!jsonLine) throw new Error(result.stderr || result.stdout || `exit ${result.status}`);
  const parsed = JSON.parse(jsonLine);
  if (!parsed.ok) throw new Error(parsed.error || "worker failed");
  return parsed.data;
}

console.log(JSON.stringify(runWorker("mate_torso_compute_shelf", { path: ASM, save: true }), null, 2));
