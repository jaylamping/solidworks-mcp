/**
 * Idempotent repair: ensure torso asm uses marengo_shoulder_pitch_mount_bracket_* parts
 * and marengo_shoulder_pitch_mount_bracket_* / actuator_rs03_*_shoulder_pitch instances.
 */
import { runWorker } from "./lib/worker-client.mjs";

const TORSO_ASM =
  "C:/code/marengo/hardware/cad/assemblies/marengo_torso_asm.SLDASM";
const PARTS_DIR = "C:/code/marengo/hardware/cad/parts";

const BRACKETS = [
  {
    part: `${PARTS_DIR}/marengo_shoulder_pitch_mount_bracket_left.SLDPRT`,
    component: "marengo_shoulder_pitch_mount_bracket_left",
  },
  {
    part: `${PARTS_DIR}/marengo_shoulder_pitch_mount_bracket_right.SLDPRT`,
    component: "marengo_shoulder_pitch_mount_bracket_right",
  },
];

const ACTUATORS = [
  ["actuator_rs03_left_shoulder_roll", "actuator_rs03_left_shoulder_pitch"],
  ["actuator_rs03_right_shoulder_roll", "actuator_rs03_right_shoulder_pitch"],
];

const steps = [];

function findComponent(components, token) {
  return components.find(
    (c) =>
      String(c.name).replace(/-\d+$/, "") === token ||
      String(c.name).includes(token) ||
      String(c.path).replace(/\\/g, "/").includes(token),
  );
}

try {
  for (const item of BRACKETS) {
    const components = runWorker("list_components", { path: TORSO_ASM }).components;
    const bracket = findComponent(components, item.component);
    steps.push({ step: `inspect_${item.component}`, result: bracket });

    if (bracket) {
      const bracketPath = String(bracket.path).replace(/\\/g, "/");
      if (!bracketPath.endsWith(`${item.component}.SLDPRT`)) {
        steps.push({
          step: `replace_${item.component}`,
          result: runWorker("replace_components_by_path", {
            path: TORSO_ASM,
            from_part_path: bracketPath,
            to_part_path: item.part,
            configuration: "Default",
            save: true,
          }),
        });
      }
    }
  }

  for (const [from, to] of ACTUATORS) {
    try {
      steps.push({
        step: `rename_${from}`,
        result: runWorker("rename_component", { path: TORSO_ASM, from, to }),
      });
    } catch (error) {
      steps.push({
        step: `rename_${from}`,
        skipped: true,
        error: error instanceof Error ? error.message : String(error),
      });
    }
  }

  steps.push({
    step: "save_torso_asm",
    result: runWorker("save_document", { path: TORSO_ASM }),
  });

  const final = runWorker("list_components", { path: TORSO_ASM }).components.filter((c) =>
    String(c.name).includes("shoulder"),
  );
  steps.push({ step: "final_shoulder_tree", result: final });

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
