import { runWorker } from "./lib/worker-client.mjs";




const steps = [];

try {
  steps.push({
    step: "unfix_layout",
    result: runWorker("set_component_fixed", {
      path: TORSO_ASM,
      component_name: "marengo_torso_layout-1",
      fixed: false,
    }),
  });

  steps.push({
    step: "unfix_frame_subasm",
    result: runWorker("set_component_fixed", {
      path: TORSO_ASM,
      component_name: "marengo_torso_frame_asm-1",
      fixed: false,
    }),
  });

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
    step: "list_mates_before_save",
    result: runWorker("list_mates", { path: TORSO_ASM }),
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