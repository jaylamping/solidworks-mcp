import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const workerProject = path.join(root, "workers/SolidWorksComWorker");

const TORSO_ASM =
  "C:/code/marengo/hardware/cad/assemblies/marengo_torso_asm_revA.SLDASM";

function runWorker(command, args) {
  const payload = JSON.stringify({ command, args });
  const result = spawnSync(
    "dotnet",
    ["run", "--project", workerProject, "--no-launch-profile"],
    {
      cwd: root,
      input: payload,
      encoding: "utf8",
      windowsHide: true,
    },
  );

  if (result.status !== 0) {
    throw new Error(`Worker exited ${result.status}: ${result.stderr || result.stdout}`);
  }

  const parsed = JSON.parse(result.stdout.trim());
  if (!parsed.ok) {
    throw new Error(parsed.error ?? "Worker failed");
  }

  return parsed.data;
}

const result = runWorker("cut_actuator_cavity", {
  path: TORSO_ASM,
  bracket_component: "marengo_shoulder_roll_mount_bracket_right_revA",
  tool_component: "actuator_rs03_right_shoulder_roll",
  clearance_mm: 0.5,
  save: true,
});

console.log(JSON.stringify({ ok: true, result }, null, 2));
