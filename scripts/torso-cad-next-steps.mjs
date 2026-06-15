import { runWorker } from "./lib/worker-client.mjs";



const FRAME_ASM = "C:/code/marengo/hardware/cad/assemblies/marengo_torso_frame_asm.SLDASM";
const TORSO_ASM = "C:/code/marengo/hardware/cad/assemblies/marengo_torso_asm.SLDASM";


const steps = [];

try {
  steps.push({
    step: "hide_layout_jig",
    result: runWorker("set_component_visible", {
      path: FRAME_ASM,
      component_name: "marengo_torso_layout",
      visible: false,
    }),
  });

  steps.push({
    step: "save_frame_asm",
    result: runWorker("save_document", { path: FRAME_ASM }),
  });

  for (const [from, to] of [
    ["rs03_waist_layout", "actuator_rs03_waist_layout"],
    ["rs03_shoulder_roll_L", "actuator_rs03_shoulder_roll_L"],
    ["rs03_shoulder_roll_R", "actuator_rs03_shoulder_roll_R"],
  ]) {
    steps.push({
      step: `rename_${from}`,
      result: runWorker("rename_component", { path: TORSO_ASM, from, to }),
    });
  }

  steps.push({
    step: "mate_frame_to_urdf_link_frame",
    result: runWorker("mate_coord_sys", {
      path: TORSO_ASM,
      component_1: "marengo_torso_layout-1",
      ref_1: "urdf_link_frame",
      component_2: "marengo_torso_layout-2",
      ref_2: "urdf_link_frame",
    }),
  });

  steps.push({
    step: "save_torso_asm",
    result: runWorker("save_document", { path: TORSO_ASM }),
  });

  console.log(JSON.stringify({ ok: true, steps }, null, 2));
} catch (error) {
  console.log(JSON.stringify({ ok: false, steps, error: error instanceof Error ? error.message : String(error) }, null, 2));
  process.exit(1);
}