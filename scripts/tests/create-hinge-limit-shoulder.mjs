/**
 * Live regression for solidworks_create_hinge_limit on marengo_arm_right_asm.
 *
 * 1) Fail-closed: wrong LimitAngle branch must restore the pre-change checkpoint
 *    when RS02 leaves the clear side (or mates go unhealthy).
 * 2) Pass path: release legacy grounding mates, build coaxial+mount+LimitAngle,
 *    sweep −5.55°…184.97°, and keep RS02 / RS03 on opposite sides.
 *
 * Does not call confirm_and_save — that remains a separate visual lock-in step.
 *
 * Usage (PowerShell):
 *   $env:SOLIDWORKS_MCP_ALLOWED_ROOTS = "\\wsl$\Ubuntu\home\joey\code\marengo;C:\code\marengo;C:\code\solidworks-mcp"
 *   node scripts/tests/create-hinge-limit-shoulder.mjs
 */
import { runWorker } from "../lib/worker-client.mjs";

const ASM =
  process.env.MARENGO_ARM_RIGHT_ASM ??
  "\\\\wsl$\\Ubuntu\\home\\joey\\code\\marengo\\cad\\assemblies\\marengo_arm_right_asm.SLDASM";

const PITCH = "marengo_shoulder_pitch_bracket_right-1";
const ROLL = "marengo_shoulder_roll_bracket_right-3";
const RS02 = "vendor_robstride_rs02_vendor-1";
const RS03 = "vendor_robstride_rs03-1";

const MIN = -5.55;
const MAX = 184.97;
const PARK = 0;
const CLEAR_SIDE_Y_MAX = -0.04;

function assert(cond, message) {
  if (!cond) throw new Error(message);
}

function yCenter(boundingBox) {
  return (boundingBox[1] + boundingBox[4]) / 2;
}

function componentY(name) {
  return yCenter(
    runWorker("get_component_box", {
      path: ASM,
      component_name: name,
    }).boundingBox,
  );
}

function listLimitNames() {
  return (runWorker("list_mates", { path: ASM }).mates || [])
    .filter((m) => String(m.name || "").startsWith("LimitAngle"))
    .map((m) => m.name);
}

function baseArgs(overrides = {}) {
  const limits = listLimitNames();
  return {
    path: ASM,
    fixed_component: PITCH,
    moving_component: ROLL,
    // Pitch owns the named roll axis / mount plane; roll bracket exposes Axis2 + Front.
    fixed_axis_ref: "shoulder_roll_axis",
    moving_axis_ref: "Axis2",
    fixed_mount_plane_ref: "shoulder_roll_mount_face_rear",
    moving_mount_plane_ref: "Front Plane",
    fixed_angle_plane_ref: "Top Plane",
    moving_angle_plane_ref: "Top Plane",
    min_angle_deg: MIN,
    max_angle_deg: MAX,
    park_angle_deg: PARK,
    align: "aligned",
    flip_dimension: false,
    release_mates: ["Coincident32", "Coincident33", ...limits],
    probe_angles_deg: [PARK, MIN, (MIN + MAX) / 2, MAX],
    clearance_component: RS02,
    clearance_axis: "y",
    clearance_max: CLEAR_SIDE_Y_MAX,
    ...overrides,
  };
}

console.log("assembly", ASM);

const beforeY = componentY(RS02);
console.log("baseline rs02Y", beforeY);

console.log("=== fail-closed: wrong flip branch must restore checkpoint ===");
const fail = runWorker("create_hinge_limit", baseArgs({ flip_dimension: true }));
assert(fail?.ok === false, `expected fail-closed ok=false, got ${fail?.ok}`);
assert(fail?.restored === true, `expected restored=true, got ${fail?.restored}`);
assert(
  typeof fail?.failureReason === "string" && fail.failureReason.length > 0,
  "expected failureReason on fail-closed path",
);
console.log(
  "fail-closed",
  JSON.stringify(
    {
      ok: fail.ok,
      restored: fail.restored,
      failureReason: fail.failureReason,
      checkpointPath: fail.checkpointPath,
    },
    null,
    2,
  ),
);

const afterFailY = componentY(RS02);
assert(
  Math.abs(afterFailY - beforeY) < 0.01,
  `fail-closed restore drifted rs02Y before=${beforeY} after=${afterFailY}`,
);

console.log("=== pass path: strict hinge + clearance sweep ===");
const pass = runWorker("create_hinge_limit", baseArgs());
assert(pass?.ok === true, `expected ok=true, got ${JSON.stringify(pass, null, 2)}`);
assert(pass?.mateHealthy === true, `mateHealthy=${pass?.mateHealthy}`);
assert(Array.isArray(pass?.samples) && pass.samples.length > 0, "missing probe samples");
assert(
  pass.samples.every((s) => s.ok === true),
  `probe sample failure: ${JSON.stringify(pass.samples)}`,
);

const rs02Y = componentY(RS02);
const rs03Y = componentY(RS03);
assert(rs02Y <= CLEAR_SIDE_Y_MAX, `RS02 clear-side fail y=${rs02Y}`);
assert(rs03Y > 0, `RS03 expected opposite/positive side y=${rs03Y}`);

runWorker("rebuild_document", { path: ASM, force: true });
const postForce = runWorker("list_mates", { path: ASM });
assert(postForce.mateHealthy === true, `post-force mateHealthy=${postForce.mateHealthy}`);
assert(
  !Array.isArray(postForce.mateFailures) || postForce.mateFailures.length === 0,
  `post-force mateFailures=${JSON.stringify(postForce.mateFailures)}`,
);

const limit = (postForce.mates || []).find((m) =>
  String(m.name || "").startsWith("LimitAngle"),
);
assert(limit != null, "LimitAngle missing after create_hinge_limit");
assert(limit.suppressed !== true, `${limit.name} suppressed`);
assert(limit.isAdvancedMate === true, `${limit.name} not advanced`);
assert(limit.flipDimension === false, `${limit.name} flipDimension=${limit.flipDimension}`);
assert(Math.abs(limit.minAngleDeg - MIN) <= 0.2, `minAngleDeg=${limit.minAngleDeg}`);
assert(Math.abs(limit.maxAngleDeg - MAX) <= 0.2, `maxAngleDeg=${limit.maxAngleDeg}`);

console.log("PASS");
console.log(
  JSON.stringify(
    {
      limitMateName: pass.limitMateName,
      axisMateName: pass.axisMateName,
      mountMateName: pass.mountMateName,
      rs02Y,
      rs03Y,
      samples: pass.samples?.map((s) => ({
        requestedDeg: s.requestedDeg,
        clearanceCenter: s.clearanceCenter,
        ok: s.ok,
      })),
    },
    null,
    2,
  ),
);
