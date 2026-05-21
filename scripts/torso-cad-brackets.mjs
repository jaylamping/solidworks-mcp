import { spawnSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const workerProject = path.join(root, "workers/SolidWorksComWorker");

const FRAME_ASM = "C:/code/marengo/hardware/cad/assemblies/marengo_torso_frame_asm_revA.SLDASM";
const TORSO_ASM = "C:/code/marengo/hardware/cad/assemblies/marengo_torso_asm_revA.SLDASM";
const BRACKET_RENAMES = 16;

function runWorker(command, args) {
  const payload = JSON.stringify({ command, args });
  const result = spawnSync("dotnet", ["run", "--project", workerProject, "--no-launch-profile"], {
    cwd: root,
    input: payload,
    encoding: "utf8",
    windowsHide: true,
  });

  if (result.status !== 0) {
    throw new Error(`Worker exited ${result.status}: ${result.stderr || result.stdout}`);
  }

  const parsed = JSON.parse(result.stdout.trim());
  if (!parsed.ok) {
    throw new Error(parsed.error ?? "Worker failed");
  }

  return parsed.data;
}

const steps = [];

try {
  for (let i = 0; i < BRACKET_RENAMES; i++) {
    steps.push({
      step: `rename_bracket_${i + 1}`,
      result: runWorker("rename_component", {
        path: FRAME_ASM,
        from: "2020_corner_bracket",
        to: "bracket_2028_corner",
      }),
    });
  }

  steps.push({
    step: "save_frame_asm",
    result: runWorker("save_document", { path: FRAME_ASM }),
  });

  for (const [from, to] of [
    ["actuator_rs03_waist_layout", "actuator_rs03_waist_yaw"],
    ["actuator_rs03_shoulder_roll_L", "actuator_rs03_left_shoulder_roll"],
    ["actuator_rs03_shoulder_roll_R", "actuator_rs03_right_shoulder_roll"],
  ]) {
    steps.push({
      step: `rename_${from}`,
      result: runWorker("rename_component", { path: TORSO_ASM, from, to }),
    });
  }

  steps.push({
    step: "save_torso_asm",
    result: runWorker("save_document", { path: TORSO_ASM }),
  });

  console.log(JSON.stringify({ ok: true, steps }, null, 2));
} catch (error) {
  console.log(
    JSON.stringify(
      { ok: false, steps, error: error instanceof Error ? error.message : String(error) },
      null,
      2,
    ),
  );
  process.exit(1);
}
