import { runWorker } from "./lib/worker-client.mjs";



const FRAME_ASM = "C:/code/marengo/hardware/cad/assemblies/marengo_torso_frame_asm_revA.SLDASM";
const TORSO_ASM = "C:/code/marengo/hardware/cad/assemblies/marengo_torso_asm_revA.SLDASM";
const BRACKET_RENAMES = 16;


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