import { runWorker } from "./lib/worker-client.mjs";



const TORSO_ASM =
  "C:/code/marengo/hardware/cad/assemblies/marengo_torso_asm.SLDASM";
const RIGHT_PART =
  "C:/code/marengo/hardware/cad/parts/marengo_shoulder_pitch_mount_bracket_right.SLDPRT";
const LEFT_PART =
  "C:/code/marengo/hardware/cad/parts/marengo_shoulder_pitch_mount_bracket_left.SLDPRT";
const LEFT_COMPONENT = "marengo_shoulder_pitch_mount_bracket_left";

// Mirror about assembly YZ plane (negate X) â€” same as MirrorXMatrix in worker.
const MIRROR_X = [-1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];


const steps = [];

try {
  steps.push({
    step: "duplicate_right_to_left_file",
    result: runWorker("export", {
      path: RIGHT_PART,
      output_path: LEFT_PART,
      start_if_missing: true,
    }),
  });

  steps.push({
    step: "set_left_props",
    result: runWorker("set_custom_properties", {
      path: LEFT_PART,
      properties: {
        process: "print",
        material: "PETG",
        revision: "A",
        owner: process.env.MARENGO_CAD_OWNER ?? "Joey Lamping",
      },
      save: true,
    }),
  });

  const components = runWorker("list_components", { path: TORSO_ASM }).components;
  const existingLeft = components.find((c) => c.name.startsWith(LEFT_COMPONENT));
  if (!existingLeft) {
    steps.push({
      step: "insert_left",
      result: runWorker("insert_component", {
        path: TORSO_ASM,
        part_path: LEFT_PART,
        name: LEFT_COMPONENT,
        save: false,
      }),
    });
  } else {
    steps.push({ step: "insert_left", result: { skipped: true, existing: existingLeft.name } });
  }

  steps.push({
    step: "mirror_left_in_asm",
    result: runWorker("set_component_transform", {
      path: TORSO_ASM,
      component_name: LEFT_COMPONENT,
      matrix: MIRROR_X,
      fix: true,
    }),
  });

  steps.push({
    step: "save_assembly",
    result: runWorker("save_document", { path: TORSO_ASM }),
  });

  steps.push({
    step: "left_bracket_box",
    result: runWorker("get_component_box", {
      path: TORSO_ASM,
      component_name: `${LEFT_COMPONENT}-1`,
    }),
  });

  steps.push({
    step: "left_motor_box",
    result: runWorker("get_component_box", {
      path: TORSO_ASM,
      component_name: "actuator_rs03_left_shoulder_pitch-3",
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