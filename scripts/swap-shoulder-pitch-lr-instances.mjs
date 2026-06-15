/**
 * Swap left/right shoulder pitch instance names in marengo_torso_asm.
 * Names only — never replace bracket part paths (mirrored geometry stays put).
 */
import { runWorker } from "./lib/worker-client.mjs";

const TORSO = "C:/code/marengo/hardware/cad/assemblies/marengo_torso_asm.SLDASM";

const ACTUATOR_LEFT = "actuator_rs03_left_shoulder_pitch";
const ACTUATOR_RIGHT = "actuator_rs03_right_shoulder_pitch";
const BRACKET_LEFT = "marengo_shoulder_pitch_mount_bracket_left";
const BRACKET_RIGHT = "marengo_shoulder_pitch_mount_bracket_right";

const swapActuatorsOnly = process.argv.includes("--actuators-only");

function stripSuffix(name) {
  return String(name).replace(/-\d+$/, "");
}

function findByPrefix(components, prefix) {
  return components.find((c) => stripSuffix(c.name) === prefix);
}

function pitchComponents() {
  return runWorker("list_components", { path: TORSO }).components.filter(
    (c) =>
      !String(c.name).includes("/") &&
      (String(c.name).includes("shoulder_pitch") ||
        String(c.name).includes("pitch_mount_bracket")),
  );
}

function centerXMm(componentName) {
  const box = runWorker("get_component_box", {
    path: TORSO,
    component_name: componentName,
  }).boundingBox;
  return ((box[0] + box[3]) / 2) * 1000;
}

function snapshot() {
  const components = pitchComponents();
  const out = {};
  for (const c of components) {
    const base = stripSuffix(c.name);
    out[base] = {
      name: c.name,
      path: String(c.path).replace(/\\/g, "/"),
      xMm: centerXMm(c.name),
    };
  }
  return out;
}

function renameComponent(from, to) {
  return runWorker("rename_component", { path: TORSO, from, to });
}

function swapPair(leftPrefix, rightPrefix, tmpPrefix) {
  renameComponent(leftPrefix, tmpPrefix);
  renameComponent(rightPrefix, leftPrefix);
  renameComponent(tmpPrefix, rightPrefix);
}

function slotKey(xMm) {
  return xMm < 0 ? "negX" : "posX";
}

function slotSnapshot(snap) {
  const slots = { negX: {}, posX: {} };
  for (const [base, row] of Object.entries(snap)) {
    slots[slotKey(row.xMm)][base] = row;
  }
  return slots;
}

function sideSortedRows(slots, side) {
  return Object.entries(slots[side])
    .map(([base, row]) => ({ base, ...row }))
    .sort((a, b) => a.xMm - b.xMm);
}

function alreadySwapped(snap) {
  const slots = slotSnapshot(snap);
  const negX = Object.keys(slots.negX);
  const posX = Object.keys(slots.posX);
  if (swapActuatorsOnly) {
    return negX.includes(ACTUATOR_RIGHT) && posX.includes(ACTUATOR_LEFT);
  }
  return (
    negX.includes(ACTUATOR_RIGHT) &&
    negX.includes(BRACKET_RIGHT) &&
    posX.includes(ACTUATOR_LEFT) &&
    posX.includes(BRACKET_LEFT)
  );
}

function verifySwap(beforeSlots, afterSlots) {
  const errors = [];

  for (const side of ["negX", "posX"]) {
    const beforePaths = sideSortedRows(beforeSlots, side).map((r) => r.path);
    const afterPaths = sideSortedRows(afterSlots, side).map((r) => r.path);
    if (JSON.stringify(beforePaths) !== JSON.stringify(afterPaths)) {
      errors.push(`part paths changed on ${side}`);
    }

    const beforeXs = sideSortedRows(beforeSlots, side).map((r) => r.xMm);
    const afterXs = sideSortedRows(afterSlots, side).map((r) => r.xMm);
    for (let i = 0; i < beforeXs.length; i += 1) {
      if (Math.abs(beforeXs[i] - afterXs[i]) > 0.5) {
        errors.push(`${side} geometry moved (${beforeXs[i]} -> ${afterXs[i]} mm)`);
      }
    }
  }

  const negXNames = Object.keys(afterSlots.negX);
  const posXNames = Object.keys(afterSlots.posX);
  if (swapActuatorsOnly) {
    if (!negXNames.includes(ACTUATOR_RIGHT)) {
      errors.push("negX slot missing right actuator instance name");
    }
    if (!posXNames.includes(ACTUATOR_LEFT)) {
      errors.push("posX slot missing left actuator instance name");
    }
    const negBracket = negXNames.find((n) => n.includes("pitch_mount_bracket"));
    const posBracket = posXNames.find((n) => n.includes("pitch_mount_bracket"));
    if (negBracket !== BRACKET_LEFT) {
      errors.push("negX bracket instance name should stay left (matches part file)");
    }
    if (posBracket !== BRACKET_RIGHT) {
      errors.push("posX bracket instance name should stay right (matches part file)");
    }
  } else {
    if (!negXNames.includes(ACTUATOR_RIGHT) || !negXNames.includes(BRACKET_RIGHT)) {
      errors.push("negX slot missing right instance names");
    }
    if (!posXNames.includes(ACTUATOR_LEFT) || !posXNames.includes(BRACKET_LEFT)) {
      errors.push("posX slot missing left instance names");
    }
  }

  return errors;
}

try {
  runWorker("open", { path: TORSO });

  const before = snapshot();
  const beforeSlots = slotSnapshot(before);
  let swapped = false;

  if (!alreadySwapped(before)) {
    swapPair(ACTUATOR_LEFT, ACTUATOR_RIGHT, "_tmp_actuator_lr_swap");
    if (!swapActuatorsOnly) {
      swapPair(BRACKET_LEFT, BRACKET_RIGHT, "_tmp_bracket_lr_swap");
    }
    swapped = true;
  }

  const after = snapshot();
  const afterSlots = slotSnapshot(after);
  let errors = [];
  if (swapped) {
    errors = verifySwap(beforeSlots, afterSlots);
  } else if (!alreadySwapped(after)) {
    errors.push("expected swapped layout not present");
  }

  if (errors.length > 0) {
    throw new Error(errors.join("; "));
  }

  runWorker("save_document", { path: TORSO });

  console.log(
    JSON.stringify(
      {
        ok: true,
        swapped,
        before,
        after,
        negX: afterSlots.negX,
        posX: afterSlots.posX,
      },
      null,
      2,
    ),
  );
} catch (error) {
  console.log(
    JSON.stringify(
      {
        ok: false,
        error: error instanceof Error ? error.message : String(error),
      },
      null,
      2,
    ),
  );
  process.exit(1);
}
