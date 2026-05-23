/**
 * Repair bracket save state after rename / debug SaveAs drift.
 *
 * - Rewires torso asm to marengo_shoulder_roll_mount_bracket_right_revA.SLDPRT
 * - Copies the last known good bracket bytes if _bracket_save_test exists
 * - Saves assembly
 */
import { spawnSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const workerProject = path.join(root, "workers/SolidWorksComWorker");

const TORSO_ASM =
  "C:/code/marengo/hardware/cad/assemblies/marengo_torso_asm_revA.SLDASM";
const RIGHT_PART =
  "C:/code/marengo/hardware/cad/parts/marengo_shoulder_roll_mount_bracket_right_revA.SLDPRT";
const TEST_PART =
  "C:/code/marengo/hardware/cad/parts/_bracket_save_test.SLDPRT";
const COMPONENT = "marengo_shoulder_roll_mount_bracket_right_revA";

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
  if (fs.existsSync(TEST_PART)) {
    steps.push({
      step: "copy_test_to_right",
      result: runWorker("export", {
        path: TEST_PART,
        output_path: RIGHT_PART,
        start_if_missing: true,
      }),
    });
  }

  const components = runWorker("list_components", { path: TORSO_ASM }).components;
  const bracket = components.find((c) => c.name.startsWith(COMPONENT));
  steps.push({ step: "bracket_component", result: bracket });

  if (bracket && !bracket.path.endsWith("marengo_shoulder_roll_mount_bracket_right_revA.SLDPRT")) {
    steps.push({
      step: "replace_to_right_part",
      result: runWorker("replace_components_by_path", {
        path: TORSO_ASM,
        from_part_path: bracket.path.replace(/\\/g, "/"),
        to_part_path: RIGHT_PART,
        configuration: "Default",
        save: true,
      }),
    });
  } else {
    steps.push({
      step: "save_assembly",
      result: runWorker("save_document", { path: TORSO_ASM }),
    });
  }

  steps.push({
    step: "diagnose_right_part",
    result: runWorker("diagnose_part_save", { path: RIGHT_PART }),
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
