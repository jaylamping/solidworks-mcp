/** DESTRUCTIVE â€” requires --confirm (see torso-frame-layout-sync.mjs). */
import { runWorker, requireConfirm } from "./lib/worker-client.mjs";

requireConfirm(
  process.argv.slice(2),
  "node scripts/torso-frame-layout-sync-continue.mjs --confirm",
);

const FRAME_ASM = "C:/code/marengo/cad/assemblies/marengo_torso_frame_asm.SLDASM";

const steps = [];
const depthRails = [
  "frame_2020_bottom_left_085",
  "frame_2020_bottom_right_085",
  "frame_2020_top_left_085",
  "frame_2020_top_right_085",
];

try {
  steps.push({
    step: "mate_layout_to_origin",
    result: runWorker("mate_component_origin", {
      path: FRAME_ASM,
      component_name: "marengo_torso_layout",
      reference: "urdf_link_frame",
    }),
  });
} catch (error) {
  steps.push({
    step: "mate_layout_to_origin",
    skipped: error instanceof Error ? error.message : String(error),
  });
}

try {
  for (const from of depthRails) {
    const to = from.replace("_085", "_100");
    try {
      steps.push({
        step: `config_${from}`,
        result: runWorker("set_component_configuration", {
          path: FRAME_ASM,
          component_name: from,
          configuration: "L100",
        }),
      });
    } catch (error) {
      steps.push({
        step: `config_${from}`,
        skipped: error instanceof Error ? error.message : String(error),
        note: "Add L100 config on vendor_2020_black_extrusion (copy L085, length 100mm)",
      });
    }
    steps.push({
      step: `rename_${from}`,
      result: runWorker("rename_component", { path: FRAME_ASM, from, to }),
    });
  }

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

  steps.push({
    step: "mate_torso_frame_to_layout",
    result: runWorker("mate_coord_sys", {
      path: TORSO_ASM,
      component_1: "marengo_torso_layout-1",
      ref_1: "urdf_link_frame",
      component_2: "marengo_torso_layout-2",
      ref_2: "urdf_link_frame",
    }),
  });

  steps.push({
    step: "fix_torso_layout",
    result: runWorker("set_component_fixed", {
      path: TORSO_ASM,
      component_name: "marengo_torso_layout-1",
      fixed: true,
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