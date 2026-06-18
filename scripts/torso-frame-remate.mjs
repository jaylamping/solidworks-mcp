/** DESTRUCTIVE â€” requires --confirm. */
import { runWorker, requireConfirm } from "./lib/worker-client.mjs";

requireConfirm(process.argv.slice(2), "node scripts/torso-frame-remate.mjs --confirm");

const FRAME_ASM = "C:/code/marengo/cad/assemblies/marengo_torso_frame_asm.SLDASM";

const depthRails = [
  "frame_2020_bottom_left_100",
  "frame_2020_bottom_right_100",
  "frame_2020_top_left_100",
  "frame_2020_top_right_100",
];


const steps = [];

try {
  steps.push({
    step: "show_layout_jig",
    result: runWorker("set_component_visible", {
      path: FRAME_ASM,
      component_name: "marengo_torso_layout",
      visible: true,
    }),
  });

  steps.push({
    step: "fix_layout_jig",
    result: runWorker("set_component_fixed", {
      path: FRAME_ASM,
      component_name: "marengo_torso_layout",
      fixed: true,
    }),
  });

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
      note: "Layout is fixed; origin mate is optional if jig placement is correct.",
    });
  }

  const configs = runWorker("list_configurations", { path: VENDOR_2020 });
  steps.push({ step: "list_vendor_configs", result: configs });

  if (!configs.configurations?.includes("L100")) {
    steps.push({
      step: "create_L100_from_L085",
      result: runWorker("add_configuration_copy", {
        path: VENDOR_2020,
        from: "L085",
        to: "L100",
      }),
    });
  }

  steps.push({
    step: "set_L100_extrusion_length",
    result: runWorker("set_dimension", {
      path: VENDOR_2020,
      configuration: "L100",
      dimension: "D1@Boss-Extrude1",
      value_meters: 0.1,
    }),
  });

  steps.push({
    step: "save_vendor_2020",
    result: runWorker("save_document", { path: VENDOR_2020 }),
  });

  for (const componentName of depthRails) {
    try {
      steps.push({
        step: `config_${componentName}`,
        result: runWorker("set_component_configuration", {
          path: FRAME_ASM,
          component_name: componentName,
          configuration: "L100",
        }),
      });
    } catch (error) {
      steps.push({
        step: `config_${componentName}`,
        skipped: error instanceof Error ? error.message : String(error),
      });
    }
  }

  steps.push({
    step: "mate_count_before_save",
    result: runWorker("list_mates", { path: FRAME_ASM }),
  });

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