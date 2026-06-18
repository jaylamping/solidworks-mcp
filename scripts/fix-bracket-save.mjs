/**
 * Repair bracket save state after rename / debug SaveAs drift.
 *
 * - Rewires torso asm to marengo_shoulder_pitch_mount_bracket_right.SLDPRT
 * - Copies the last known good bracket bytes if _bracket_save_test exists
 * - Saves assembly
 */
import { runWorker } from "./lib/worker-client.mjs";



const TORSO_ASM =
  "C:/code/marengo/cad/assemblies/marengo_torso_asm.SLDASM";
const RIGHT_PART =
  "C:/code/marengo/cad/parts/marengo_shoulder_pitch_mount_bracket_right.SLDPRT";
const TEST_PART =
  "C:/code/marengo/cad/parts/_bracket_save_test.SLDPRT";
const COMPONENT = "marengo_shoulder_pitch_mount_bracket_right";


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

  if (bracket && !bracket.path.endsWith("marengo_shoulder_pitch_mount_bracket_right.SLDPRT")) {
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