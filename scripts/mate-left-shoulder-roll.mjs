import { runWorker, logStep } from "./lib/worker-client.mjs";
import { tryMateCoincident, ANTI_ALIGNED, ALIGNED } from "./lib/mate-helpers.mjs";

const TORSO_ASM =
  process.env.MARENGO_TORSO_ASM ??
  "C:/code/marengo/cad/assemblies/marengo_torso_asm.SLDASM";
const LAYOUT = process.env.TORSO_LAYOUT_COMPONENT ?? "marengo_torso_layout";
const LEFT_SHOULDER_MOTOR = process.env.LEFT_SHOULDER_MOTOR ?? "actuator_rs03_left_shoulder_pitch";
const VENDOR_PART =
  process.env.RS03_VENDOR_PART ??
  "C:/code/marengo/cad/vendor/vendor_robstride_rs03_vendor.SLDPRT";

const steps = [];

function tryCoordMate(path, layoutComponent, motorComponent, mountRef) {
  try {
    return {
      ok: true,
      result: runWorker("mate_coord_sys", {
        path,
        component_1: layoutComponent,
        ref_1: mountRef,
        component_2: motorComponent,
        ref_2: "urdf_link_frame",
      }),
    };
  } catch (error) {
    return { ok: false, error: error instanceof Error ? error.message : String(error) };
  }
}

try {
  logStep(
    "vendor_add_rs03_urdf_frame",
    runWorker("vendor_add_rs03_urdf_frame", {
      path: VENDOR_PART,
      save: true,
      replace_existing: true,
    }),
    steps,
  );

  logStep(
    "show_layout",
    runWorker("set_component_visible", {
      path: TORSO_ASM,
      component_name: LAYOUT,
      visible: true,
    }),
    steps,
  );

  logStep(
    "unfix_motor",
    runWorker("set_component_fixed", {
      path: TORSO_ASM,
      component_name: MOTOR,
      fixed: false,
    }),
    steps,
  );

  const mountRef = "shoulder_mount_left";
  const railRef = "shoulder_rail_inner_left";
  const coordMate = tryCoordMate(TORSO_ASM, LAYOUT, MOTOR, mountRef);
  logStep("coord_sys_mate", coordMate, steps);

  const mates = [];
  if (!coordMate.ok) {
    mates.push(
      logStep(
        "fallback_rail_front",
        tryMateCoincident({
          path: TORSO_ASM,
          component_1: LAYOUT,
          ref_1: railRef,
          component_2: MOTOR,
          ref_2: "Front Plane",
          align: ANTI_ALIGNED,
        }),
        steps,
      ).result,
    );

    mates.push(
      logStep(
        "fallback_shoulder_plane",
        tryMateCoincident({
          path: TORSO_ASM,
          component_1: LAYOUT,
          ref_1: "shoulder_plane",
          component_2: MOTOR,
          ref_2: "Top Plane",
          align: ALIGNED,
        }),
        steps,
      ).result,
    );

    mates.push(
      logStep(
        "fallback_front_right",
        tryMateCoincident({
          path: TORSO_ASM,
          component_1: LAYOUT,
          ref_1: "Front Plane",
          component_2: MOTOR,
          ref_2: "Right Plane",
          align: ALIGNED,
        }),
        steps,
      ).result,
    );
  }

  logStep("save_assembly", runWorker("save_document", { path: TORSO_ASM }), steps);

  logStep("list_mates_after", runWorker("list_mates", { path: TORSO_ASM }), steps);

  console.log(JSON.stringify({ ok: true, coordMate, mates, steps }, null, 2));
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
