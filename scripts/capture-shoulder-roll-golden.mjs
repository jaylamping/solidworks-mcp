import { spawnSync } from "node:child_process";
import fs from "node:fs";
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

const steps = [];

try {
  const existing = runWorker("list_components", { path: TORSO_ASM });
  const hasRight = existing.components?.some((c) =>
    String(c.name).startsWith("actuator_rs03_right_shoulder_roll"),
  );
  const vendorRight = existing.components?.find(
    (c) =>
      String(c.name).startsWith("vendor_robstride_rs03_vendor") &&
      !String(c.name).includes("left"),
  );

  if (!hasRight && vendorRight) {
    steps.push({
      step: "rename_right_shoulder_roll",
      result: runWorker("rename_component", {
        path: TORSO_ASM,
        from: vendorRight.name.replace(/-\d+$/, ""),
        to: "actuator_rs03_right_shoulder_roll",
        save: true,
      }),
    });
  }

  const captured = runWorker("capture_shoulder_roll_golden", {
    path: TORSO_ASM,
    left_component: "actuator_rs03_left_shoulder_roll",
    right_component: "actuator_rs03_right_shoulder_roll",
  });

  const golden = {
    ...captured,
    left: {
      componentPrefix: "actuator_rs03_left_shoulder_roll",
      matrix: captured.left.matrix,
      boundingBoxM: captured.left.boundingBoxM,
    },
    right: {
      componentPrefix: "actuator_rs03_right_shoulder_roll",
      matrix: captured.right.matrix,
      boundingBoxM: captured.right.boundingBoxM,
    },
  };

  fs.writeFileSync(goldenPath, `${JSON.stringify(golden, null, 2)}\n`, "utf8");
  steps.push({ step: "write_golden_json", path: goldenPath, captured: golden });

  console.log(JSON.stringify({ ok: true, steps }, null, 2));
} catch (error) {
  console.log(
    JSON.stringify(
      {
        ok: false,
        steps,
        error: error instanceof Error ? error.message : String(error),
      },
      null,
      2,
    ),
  );
  process.exit(1);
}
