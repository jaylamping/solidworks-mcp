import { runWorker, logStep } from "./lib/worker-client.mjs";
import { mateFirstThatWorks, mateShelfHeightWithFallback, tryMateCoincident } from "./lib/mate-helpers.mjs";

const ASM =
  process.env.MARENGO_TORSO_ASM ??
  "C:/code/marengo/hardware/cad/assemblies/marengo_torso_asm.SLDASM";

const SHELF = process.env.COMPUTE_SHELF_COMPONENT ?? "marengo_torso_upper_compute_shelf";
const LAYOUT = process.env.TORSO_LAYOUT_COMPONENT ?? "marengo_torso_layout";
const LEFT_ACTUATOR = process.env.LEFT_SHOULDER_ACTUATOR ?? "actuator_rs03_left_shoulder_pitch";
const RIGHT_ACTUATOR = process.env.RIGHT_SHOULDER_ACTUATOR ?? "actuator_rs03_right_shoulder_pitch";

const steps = [];

try {
  const innerAsm = logStep(
    "get_layout_inner_clear_box",
    runWorker("get_feature_box", {
      path: ASM,
      component_name: LAYOUT,
      feature_name: "torso_inner_clear",
    }),
    steps,
  ).result;

  const leftBox = logStep(
    "get_left_actuator_box",
    runWorker("get_component_box", { path: ASM, component_name: LEFT_ACTUATOR }),
    steps,
  ).result;

  const rightBox = logStep(
    "get_right_actuator_box",
    runWorker("get_component_box", { path: ASM, component_name: RIGHT_ACTUATOR }),
    steps,
  ).result;

  const inner = innerAsm.boundingBox;
  const left = leftBox.boundingBox;
  const right = rightBox.boundingBox;
  if (!inner || inner.length < 6 || !left || left.length < 6 || !right || right.length < 6) {
    throw new Error("Required bounding boxes unavailable.");
  }

  const shelfSeatAssemblyY = Math.min(left[1], right[1]);
  const centerX = (inner[0] + inner[3]) / 2;
  const centerZ = (inner[2] + inner[5]) / 2;

  const layoutPartPath = runWorker("list_components", { path: ASM }).components?.find(
    (c) => c.name?.startsWith(LAYOUT),
  )?.path;

  if (!layoutPartPath) {
    throw new Error(`Layout component path not found: ${LAYOUT}`);
  }

  const innerPart = logStep(
    "get_layout_inner_clear_part_box",
    runWorker("get_part_feature_box", {
      part_path: layoutPartPath,
      feature_name: "torso_inner_clear",
    }),
    steps,
  ).result.boundingBox;

  const shelfSeatLayoutY = innerPart[1] + (shelfSeatAssemblyY - inner[1]);

  logStep(
    "ensure_compute_shelf_seat_plane",
    runWorker("ensure_offset_plane", {
      part_path: layoutPartPath,
      plane_name: "compute_shelf_seat",
      offset_m: shelfSeatLayoutY,
      reference_plane: "Top Plane",
      save: false,
    }),
    steps,
  );

  logStep(
    "reset_shelf_transform",
    runWorker("reset_component_transform", {
      path: ASM,
      component_name: SHELF,
      unfix: true,
    }),
    steps,
  );

  logStep(
    "mate_front",
    tryMateCoincident({
      path: ASM,
      component_1: SHELF,
      ref_1: "Front Plane",
      component_2: LAYOUT,
      ref_2: "Front Plane",
    }),
    steps,
  );

  logStep(
    "mate_right",
    tryMateCoincident({
      path: ASM,
      component_1: SHELF,
      ref_1: "Right Plane",
      component_2: LAYOUT,
      ref_2: "Right Plane",
    }),
    steps,
  );

  const faceIndex = runWorker("get_planar_face_index", {
    path: ASM,
    component_name: SHELF,
    feature_name: "Boss-Extrude1",
    mode: "top",
  }).faceIndex;

  const heightMate = mateShelfHeightWithFallback({
    path: ASM,
    shelfComponent: SHELF,
    layoutComponent: LAYOUT,
    shelfFaceIndex: faceIndex,
  });
  logStep("mate_height_fallbacks", heightMate, steps);

  logStep(
    "save_layout",
    runWorker("save_document", { path: layoutPartPath }),
    steps,
  );

  logStep(
    "save_assembly",
    runWorker("save_document", { path: ASM }),
    steps,
  );

  console.log(
    JSON.stringify(
      {
        ok: true,
        shelfSeatAssemblyY,
        shelfSeatLayoutY,
        centerX,
        centerZ,
        heightOk: heightMate.heightOk,
        steps,
      },
      null,
      2,
    ),
  );
} catch (error) {
  console.error(JSON.stringify({ ok: false, error: String(error), steps }, null, 2));
  process.exit(1);
}
