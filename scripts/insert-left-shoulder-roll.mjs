import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const workerProject = path.join(root, "workers/SolidWorksComWorker");

const TORSO_ASM =
  "C:/code/marengo/hardware/cad/assemblies/marengo_torso_asm_revA.SLDASM";
const RS03_PART =
  "C:/code/marengo/hardware/cad/vendor/vendor_robstride_rs03_vendor.SLDPRT";

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
  const existing = runWorker("list_components", { path: TORSO_ASM });
  const hasLeft = existing.components?.some((c) =>
    String(c.name).startsWith("actuator_rs03_left_shoulder_roll"),
  );
  const hasVendorOnly = existing.components?.some((c) =>
    String(c.name).startsWith("vendor_robstride_rs03_vendor"),
  );

  if (!hasLeft) {
    if (hasVendorOnly) {
      steps.push({
        step: "rename_inserted_vendor_to_left_shoulder_roll",
        result: runWorker("rename_component", {
          path: TORSO_ASM,
          from: "vendor_robstride_rs03_vendor",
          to: "actuator_rs03_left_shoulder_roll",
          save: true,
        }),
      });
    } else {
      steps.push({
        step: "insert_left_shoulder_roll",
        result: runWorker("insert_component", {
          path: TORSO_ASM,
          part_path: RS03_PART,
          name: "actuator_rs03_left_shoulder_roll",
          save: true,
        }),
      });
    }
  }

  steps.push({
    step: "place_left_shoulder_roll",
    result: runWorker("place_shoulder_roll_motors", {
      path: TORSO_ASM,
      side: "left",
      left_component: "actuator_rs03_left_shoulder_roll",
      inner_rail_mm: INNER_RAIL_MM,
      save: true,
    }),
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
