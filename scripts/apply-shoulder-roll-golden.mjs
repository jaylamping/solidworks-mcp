import { readFileSync } from "node:fs";
import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const workerProject = path.join(root, "workers/SolidWorksComWorker");
const goldenPath = path.join(root, "scripts/shoulder-roll-golden.json");

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

const golden = JSON.parse(readFileSync(goldenPath, "utf8"));

const result = runWorker("apply_shoulder_roll_golden", {
  path: TORSO_ASM,
  left: golden.left,
  right: golden.right,
  save: true,
  fix: true,
});

console.log(JSON.stringify({ ok: true, goldenPath, result }, null, 2));
