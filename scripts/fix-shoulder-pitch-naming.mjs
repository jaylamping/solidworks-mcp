/**
 * One-shot: align torso shoulder CAD naming with pitch-on-torso reality.
 * - Actuator instances: *_shoulder_roll → *_shoulder_pitch
 * - Bracket parts/instances: *_shoulder_pitch_mount_bracket_* (L/R)
 * Mates preserved via rename_component + replace_components_by_path.
 */
import fs from "node:fs";
import { runWorker } from "./lib/worker-client.mjs";

const PARTS_DIR = "C:/code/marengo/cad/parts";
const EXPORTS_DIR = "C:/code/marengo/cad/exports";
const TORSO_ASM = "C:/code/marengo/cad/assemblies/marengo_torso_asm.SLDASM";

const PITCH_BRACKET_LEFT = `${PARTS_DIR}/marengo_shoulder_pitch_mount_bracket_left.SLDPRT`;
const PITCH_BRACKET_RIGHT = `${PARTS_DIR}/marengo_shoulder_pitch_mount_bracket_right.SLDPRT`;
const ROLL_BRACKET_LEFT = `${PARTS_DIR}/marengo_shoulder_roll_mount_bracket_left.SLDPRT`;
const ROLL_BRACKET_RIGHT = `${PARTS_DIR}/marengo_shoulder_roll_mount_bracket_right.SLDPRT`;

const ACTUATOR_RENAMES = [
  ["actuator_rs03_left_shoulder_roll", "actuator_rs03_left_shoulder_pitch"],
  ["actuator_rs03_right_shoulder_roll", "actuator_rs03_right_shoulder_pitch"],
];

const BRACKET_COMPONENT_RENAMES = [
  ["marengo_shoulder_roll_mount_bracket_left", "marengo_shoulder_pitch_mount_bracket_left"],
  ["marengo_shoulder_pitch_mount_bracket_left", "marengo_shoulder_pitch_mount_bracket_left"],
  ["marengo_shoulder_roll_mount_bracket_right", "marengo_shoulder_pitch_mount_bracket_right"],
  ["marengo_shoulder_pitch_mount_bracket_right", "marengo_shoulder_pitch_mount_bracket_right"],
];

const steps = [];

function stripSuffix(name) {
  return String(name).replace(/-\d+$/, "");
}

function findComponent(components, token) {
  return components.find(
    (c) =>
      stripSuffix(c.name) === token ||
      String(c.name).includes(token) ||
      String(c.path).replace(/\\/g, "/").includes(token),
  );
}

function renameComponent(from, to) {
  if (from === to) {
    return { skipped: true, from, to };
  }
  try {
    return runWorker("rename_component", { path: TORSO_ASM, from, to });
  } catch (error) {
    const msg = error instanceof Error ? error.message : String(error);
    if (msg.includes("Component not found")) {
      return { skipped: true, from, to, reason: "not_found" };
    }
    throw error;
  }
}

function ensureLeftBracketPart() {
  if (fs.existsSync(PITCH_BRACKET_LEFT)) {
    steps.push({ step: "left_bracket_part_exists", path: PITCH_BRACKET_LEFT });
    return;
  }
  if (!fs.existsSync(ROLL_BRACKET_LEFT)) {
    throw new Error(`Missing left bracket source: ${ROLL_BRACKET_LEFT}`);
  }
  steps.push({
    step: "save_as_left_pitch_bracket",
    result: runWorker("export", {
      path: ROLL_BRACKET_LEFT,
      output_path: PITCH_BRACKET_LEFT,
      start_if_missing: true,
    }),
  });
}

function rewireBracketPart(oldToken, newPartPath, newComponent) {
  const components = runWorker("list_components", { path: TORSO_ASM }).components;
  const bracket = findComponent(components, oldToken) ?? findComponent(components, newComponent);
  if (!bracket) {
    steps.push({ step: `bracket_missing_${newComponent}`, skipped: true });
    return;
  }

  const bracketPath = String(bracket.path).replace(/\\/g, "/");
  const targetName = `${newComponent}.SLDPRT`;
  if (!bracketPath.endsWith(targetName)) {
    steps.push({
      step: `replace_${newComponent}`,
      result: runWorker("replace_components_by_path", {
        path: TORSO_ASM,
        from_part_path: bracketPath,
        to_part_path: newPartPath,
        configuration: "Default",
        save: true,
      }),
    });
  }
}

try {
  ensureLeftBracketPart();

  rewireBracketPart("marengo_shoulder_roll_mount_bracket_left", PITCH_BRACKET_LEFT, "marengo_shoulder_pitch_mount_bracket_left");
  rewireBracketPart("marengo_shoulder_roll_mount_bracket_right", PITCH_BRACKET_RIGHT, "marengo_shoulder_pitch_mount_bracket_right");
  rewireBracketPart("marengo_shoulder_pitch_mount_bracket_right", PITCH_BRACKET_RIGHT, "marengo_shoulder_pitch_mount_bracket_right");

  for (const [from, to] of ACTUATOR_RENAMES) {
    steps.push({ step: `rename_${from}`, result: renameComponent(from, to) });
  }

  for (const [from, to] of BRACKET_COMPONENT_RENAMES) {
    steps.push({ step: `rename_${from}`, result: renameComponent(from, to) });
  }

  steps.push({
    step: "save_torso_asm",
    result: runWorker("save_document", { path: TORSO_ASM }),
  });

  const finalComponents = runWorker("list_components", { path: TORSO_ASM }).components.filter(
    (c) =>
      String(c.name).includes("shoulder") &&
      (String(c.name).includes("actuator") || String(c.name).includes("mount_bracket")),
  );
  steps.push({ step: "final_shoulder_components", result: finalComponents });

  steps.push({
    step: "mate_count",
    result: runWorker("list_mates", { path: TORSO_ASM }).mateCount,
  });

  // Remove superseded part files after successful asm save.
  for (const obsolete of [ROLL_BRACKET_LEFT, ROLL_BRACKET_RIGHT]) {
    if (fs.existsSync(obsolete) && obsolete !== PITCH_BRACKET_LEFT && obsolete !== PITCH_BRACKET_RIGHT) {
      try {
        fs.unlinkSync(obsolete);
        steps.push({ step: "removed_obsolete_part", path: obsolete });
      } catch (error) {
        steps.push({
          step: "removed_obsolete_part_failed",
          path: obsolete,
          error: error instanceof Error ? error.message : String(error),
        });
      }
    }
  }

  const orphan = `${PARTS_DIR}/marengo_shoulder_roll_bracket_right.SLDPRT`;
  if (fs.existsSync(orphan)) {
    try {
      fs.unlinkSync(orphan);
      steps.push({ step: "removed_orphan_part", path: orphan });
    } catch (error) {
      steps.push({
        step: "removed_orphan_part_failed",
        path: orphan,
        error: error instanceof Error ? error.message : String(error),
      });
    }
  }

  // Rename exported STLs if present.
  const stlRenames = [
    ["marengo_shoulder_roll_mount_bracket_left.STL", "marengo_shoulder_pitch_mount_bracket_left.STL"],
    ["marengo_shoulder_roll_mount_bracket_right.STL", "marengo_shoulder_pitch_mount_bracket_right.STL"],
  ];
  for (const [from, to] of stlRenames) {
    const fromPath = `${EXPORTS_DIR}/${from}`;
    const toPath = `${EXPORTS_DIR}/${to}`;
    if (fs.existsSync(fromPath) && !fs.existsSync(toPath)) {
      fs.renameSync(fromPath, toPath);
      steps.push({ step: "renamed_stl", from: fromPath, to: toPath });
    }
  }

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
