/**
 * Shoulder-roll clearance + limit mate regression for marengo_arm_right_asm.
 *
 * Catches the failure mode where roll travel swings into the shoulder:
 * at the parked / nominal pose, the RS02 actuator bbox center must sit on the
 * clear side of the joint (negative assembly Y for this right-arm frame).
 *
 * Also asserts LimitAngle sense/stops and that roll/RS02 stay float.
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

const failures = [];
function check(cond, message) {
  if (!cond) failures.push(message);
}

console.log("assembly", ASM);

const comps = runWorker("list_components", { path: ASM }).components || [];
const rollComp = comps.find((c) => c.name === ROLL);
const rs02Comp = comps.find((c) => c.name === RS02);
check(rollComp != null, `missing component ${ROLL}`);
check(rs02Comp != null, `missing component ${RS02}`);
check(rollComp?.isFixed !== true, `${ROLL} is Fixed (cannot drag-rotate)`);
check(rs02Comp?.isFixed !== true, `${RS02} is Fixed (locks roll via attach mates)`);

const rs02Box = runWorker("get_component_box", {
  path: ASM,
  component_name: RS02,
}).boundingBox;
const rs02Y = yCenter(rs02Box);
console.log("rs02 Y center", rs02Y);
check(
  rs02Y <= CLEAR_SIDE_Y_MAX,
  `roll travel is on the shoulder-collision side (rs02 Y=${rs02Y}, need <= ${CLEAR_SIDE_Y_MAX})`,
);

const listed = runWorker("list_mates", { path: ASM });
const limit = findLimitMate(listed.mates || []);
assert(limit, "No LimitAngle* mate found");
console.log("limit mate", JSON.stringify(limit, null, 2));

check(
  limit.flipDimension !== undefined && limit.minAngleDeg !== undefined && limit.maxAngleDeg !== undefined,
  "list_mates must expose flipDimension, minAngleDeg, maxAngleDeg for angle mates",
);
check(limit.suppressed !== true, `${limit.name} is suppressed`);
check((limit.errorCode ?? 0) === 0, `${limit.name} errorCode=${limit.errorCode}`);
check(limit.isAdvancedMate === true, `${limit.name} isAdvancedMate=${limit.isAdvancedMate}`);
check(limit.flipDimension === FLIP, `${limit.name} flipDimension=${limit.flipDimension}, expected ${FLIP}`);

if (typeof limit.minAngleDeg === "number" && typeof limit.maxAngleDeg === "number") {
  try {
    near(limit.minAngleDeg, EXPECT_MIN, LIMIT_TOL_DEG, "minAngleDeg");
  } catch (e) {
    failures.push(e.message);
  }
  try {
    near(limit.maxAngleDeg, EXPECT_MAX, LIMIT_TOL_DEG, "maxAngleDeg");
  } catch (e) {
    failures.push(e.message);
  }
}

// Ground mates that pin RS02 to the assembly must stay suppressed.
for (const mateName of ["Perpendicular2", "Coincident40", "Coincident87"]) {
  const mate = (listed.mates || []).find((m) => m.name === mateName);
  if (mate) {
    check(mate.suppressed === true, `${mateName} must stay suppressed (grounds RS02 / locks roll)`);
  }
}

if (failures.length) {
  console.error("FAIL");
  for (const f of failures) console.error(" -", f);
  process.exit(1);
}

console.log("PASS");
console.log(
  JSON.stringify(
    {
      rs02Y,
      clearSideYMax: CLEAR_SIDE_Y_MAX,
      mate: {
        name: limit.name,
        flipDimension: limit.flipDimension,
        minAngleDeg: limit.minAngleDeg,
        maxAngleDeg: limit.maxAngleDeg,
        angleDeg: limit.angleDeg,
        errorCode: limit.errorCode,
      },
    },
    null,
    2,
  ),
);
