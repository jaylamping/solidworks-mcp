/**
 * Shoulder-roll clearance + limit mate regression for marengo_arm_right_asm.
 *
 * Catches the failure mode where roll travel swings into the shoulder:
 * at the parked / nominal pose, the RS02 actuator bbox center must sit on the
 * clear side of the joint (negative assembly Y for this right-arm frame).
 *
 * Requires a healthy advanced LimitAngle on the roll DOF (Top↔Top about
 * shoulder_roll_axis, parked clear-side) plus force-rebuild / confirm_and_save
 * health. Soft list_mates alone is insufficient — SolidWorks can report
 * errorCode 0 until ForceRebuild3 / What's Wrong surfaces failures.
 *
 * Usage (PowerShell):
 *   $env:SOLIDWORKS_MCP_ALLOWED_ROOTS = "\\wsl$\Ubuntu\home\joey\code\marengo;C:\code\marengo"
 *   node scripts/tests/shoulder-roll-limit-angle.mjs
 */
import { runWorker } from "../lib/worker-client.mjs";

const ASM =
  process.env.MARENGO_ARM_RIGHT_ASM ??
  "\\\\wsl$\\Ubuntu\\home\\joey\\code\\marengo\\cad\\assemblies\\marengo_arm_right_asm.SLDASM";
const ROLL = "marengo_shoulder_roll_bracket_right-3";
const RS02 = "vendor_robstride_rs02_vendor-1";

const EXPECT_MIN = -5.55;
const EXPECT_MAX = 184.97;
const LIMIT_TOL_DEG = 0.2;
const FLIP = false;
// Colliding stop measured ~+0.079 m; clear stop ~-0.079 m.
const CLEAR_SIDE_Y_MAX = -0.04;

function assert(cond, message) {
  if (!cond) throw new Error(message);
}

function near(actual, expected, tol, label) {
  assert(
    typeof actual === "number" && Math.abs(actual - expected) <= tol,
    `${label}: expected ${expected}±${tol}, got ${actual}`,
  );
}

function findLimitMate(mates) {
  return mates.find(
    (m) => String(m.type || "").includes("Limit") || String(m.name || "").startsWith("LimitAngle"),
  );
}

function yCenter(boundingBox) {
  return (boundingBox[1] + boundingBox[4]) / 2;
}

function listMatesPayload() {
  return runWorker("list_mates", { path: ASM });
}

function rs02YCenter() {
  return yCenter(
    runWorker("get_component_box", {
      path: ASM,
      component_name: RS02,
    }).boundingBox,
  );
}

const failures = [];
function check(cond, message) {
  if (!cond) failures.push(message);
}

function assertAssemblyHealthy(label) {
  const payload = listMatesPayload();
  const mates = payload.mates || [];
  const limit = findLimitMate(mates);
  const bad = payload.mateFailures || [];
  const y = rs02YCenter();
  console.log(
    label,
    JSON.stringify(
      {
        rs02Y: y,
        mateHealthy: payload.mateHealthy,
        bad,
        limit: limit && {
          name: limit.name,
          errorCode: limit.errorCode,
          flipDimension: limit.flipDimension,
          minAngleDeg: limit.minAngleDeg,
          maxAngleDeg: limit.maxAngleDeg,
          angleDeg: limit.angleDeg,
          suppressed: limit.suppressed,
        },
      },
      null,
      2,
    ),
  );

  check(payload.mateHealthy === true, `${label}: mateHealthy=${payload.mateHealthy}`);
  check(
    bad.length === 0,
    `${label}: unsolved mates: ${bad.map((m) => `${m.name}:${m.errorCode}`).join(", ")}`,
  );
  check(y <= CLEAR_SIDE_Y_MAX, `${label}: clear-side fail rs02Y=${y}`);
  check(limit != null, `${label}: No LimitAngle* mate found`);

  for (const mateName of ["Perpendicular2", "Coincident40", "Coincident87"]) {
    const mate = mates.find((m) => m.name === mateName);
    if (mate) {
      check(
        mate.suppressed === true,
        `${label}: ${mateName} must stay suppressed (grounds RS02 / locks roll)`,
      );
    }
  }

  if (!limit) return { mates, limit: null, y };

  check(limit.suppressed !== true, `${label}: ${limit.name} is suppressed`);
  check(limit.errorCode === 0, `${label}: ${limit.name} errorCode=${limit.errorCode}`);
  check(limit.isAdvancedMate === true, `${label}: ${limit.name} isAdvancedMate=${limit.isAdvancedMate}`);
  check(
    limit.flipDimension === FLIP,
    `${label}: ${limit.name} flipDimension=${limit.flipDimension}, expected ${FLIP}`,
  );
  check(
    limit.flipDimension !== undefined && limit.minAngleDeg !== undefined && limit.maxAngleDeg !== undefined,
    `${label}: list_mates must expose flipDimension, minAngleDeg, maxAngleDeg`,
  );

  if (typeof limit.minAngleDeg === "number" && typeof limit.maxAngleDeg === "number") {
    try {
      near(limit.minAngleDeg, EXPECT_MIN, LIMIT_TOL_DEG, `${label} minAngleDeg`);
    } catch (e) {
      failures.push(e.message);
    }
    try {
      near(limit.maxAngleDeg, EXPECT_MAX, LIMIT_TOL_DEG, `${label} maxAngleDeg`);
    } catch (e) {
      failures.push(e.message);
    }
  }

  return { mates, limit, y };
}

console.log("assembly", ASM);

const comps = runWorker("list_components", { path: ASM }).components || [];
const rollComp = comps.find((c) => c.name === ROLL);
const rs02Comp = comps.find((c) => c.name === RS02);
check(rollComp != null, `missing component ${ROLL}`);
check(rs02Comp != null, `missing component ${RS02}`);
check(rollComp?.isFixed !== true, `${ROLL} is Fixed (cannot drag-rotate)`);
check(rs02Comp?.isFixed !== true, `${RS02} is Fixed (locks roll via attach mates)`);

assertAssemblyHealthy("pre-force");

console.log("force rebuild");
runWorker("rebuild_document", { path: ASM, force: true });
assertAssemblyHealthy("post-force-rebuild");

console.log("confirm_and_save (server hard gate)");
const confirm = runWorker("confirm_and_save", {
  path: ASM,
  looks_good: true,
  confirm: true,
  reopen: true,
});
check(confirm?.ok === true, `confirm_and_save ok=${confirm?.ok}`);
check(confirm?.saved === true, `confirm_and_save saved=${confirm?.saved}`);
check(confirm?.poseStable === true, `confirm_and_save poseStable=${confirm?.poseStable}`);
check(
  !Array.isArray(confirm?.preMateFailures) || confirm.preMateFailures.length === 0,
  `confirm_and_save preMateFailures=${JSON.stringify(confirm?.preMateFailures)}`,
);
check(
  !Array.isArray(confirm?.postSaveMateFailures) || confirm.postSaveMateFailures.length === 0,
  `confirm_and_save postSaveMateFailures=${JSON.stringify(confirm?.postSaveMateFailures)}`,
);
check(
  !Array.isArray(confirm?.postReopenMateFailures) || confirm.postReopenMateFailures.length === 0,
  `confirm_and_save postReopenMateFailures=${JSON.stringify(confirm?.postReopenMateFailures)}`,
);

const final = assertAssemblyHealthy("post-confirm-and-save");

if (failures.length) {
  console.error("FAIL");
  for (const f of failures) console.error(" -", f);
  process.exit(1);
}

console.log("PASS");
console.log(
  JSON.stringify(
    {
      rs02Y: final.y,
      clearSideYMax: CLEAR_SIDE_Y_MAX,
      mate: final.limit && {
        name: final.limit.name,
        flipDimension: final.limit.flipDimension,
        minAngleDeg: final.limit.minAngleDeg,
        maxAngleDeg: final.limit.maxAngleDeg,
        angleDeg: final.limit.angleDeg,
        errorCode: final.limit.errorCode,
      },
      confirm,
    },
    null,
    2,
  ),
);
