import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const workerProject = path.join(root, "workers/SolidWorksComWorker");

const TORSO_ASM =
  "C:/code/marengo/hardware/cad/assemblies/marengo_torso_asm_revA.SLDASM";

const INNER_RAIL_MM = 55;

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
  steps.push({
    step: "vendor_add_rs03_urdf_frame",
    result: runWorker("vendor_add_rs03_urdf_frame", {
      path: "C:/code/marengo/hardware/cad/vendor/vendor_robstride_rs03_vendor.SLDPRT",
      save: true,
      replace_existing: true,
    }),
  });

  steps.push({
    step: "mate_left_shoulder_roll",
    result: runWorker("mate_shoulder_roll_motor", {
      path: TORSO_ASM,
      side: "left",
      motor_component: "actuator_rs03_left_shoulder_roll",
      layout_component: "marengo_torso_layout_revA",
      inner_rail_mm: INNER_RAIL_MM,
      preplace: false,
      save: true,
    }),
  });

  steps.push({
    step: "list_mates_after",
    result: runWorker("list_mates", { path: TORSO_ASM }),
  });

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
