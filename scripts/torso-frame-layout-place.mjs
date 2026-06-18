/**
 * DESTRUCTIVE: overwrites transforms on 12 rails + 16 brackets in the frame assembly.
 * Only run when you explicitly want a layout snap — never from agents or wrapper scripts by default.
 *
 *   node scripts/torso-frame-layout-place.mjs --confirm
 */
import { spawnSync } from "node:child_process";

const argv = process.argv.slice(2);
if (!argv.includes("--confirm")) {
  console.error(
    "Refusing to run: torso-frame-layout-place.mjs moves and fixes frame components in SolidWorks.\n" +
      "Pass --confirm only when you intend to apply layout ICE placement:\n" +
      "  node scripts/torso-frame-layout-place.mjs --confirm",
  );
  process.exit(1);
}

const root = "c:/code/solidworks-mcp";
const workerProject = `${root}/workers/SolidWorksComWorker`;
const FRAME = "C:/code/marengo/cad/assemblies/marengo_torso_frame_asm.SLDASM";
const VENDOR = "C:/code/marengo/cad/vendor/vendor_2020_black_extrusion.SLDPRT";
const BRACKET_PART =
  "C:/code/marengo/cad/vendor/vendor_2028_corner_bracket_vendor.SLDPRT";
const LAYOUT = "marengo_torso_layout";

function run(command, args) {
  const r = spawnSync("dotnet", ["run", "--project", workerProject, "--no-launch-profile"], {
    cwd: root,
    input: JSON.stringify({ command, args }),
    encoding: "utf8",
    windowsHide: true,
  });
  if (r.status !== 0) throw new Error(r.stderr || r.stdout);
  const o = JSON.parse(r.stdout);
  if (!o.ok) throw new Error(o.error);
  return o.data;
}

function iceBox(feature) {
  const { boundingBox: b } = run("get_feature_box", {
    path: FRAME,
    component_name: LAYOUT,
    feature_name: feature,
  });
  return b;
}

function center(b) {
  return [(b[0] + b[3]) / 2, (b[1] + b[4]) / 2, (b[2] + b[5]) / 2];
}

function setLen(cfg, len) {
  run("set_dimension", {
    path: VENDOR,
    configuration: cfg,
    dimension: "D1@Boss-Extrude1",
    value_meters: len,
  });
}

// --- vendor cut lengths ---
for (const cfg of ["L085", "L100", "L110", "L340", "L380", "L500"]) {
  for (const f of ["Cut-Extrude1", "Cut-Extrude2", "Cut-Extrude3", "Cut-Extrude4"]) {
    run("set_feature_suppression", { path: VENDOR, configuration: cfg, feature_name: f, suppressed: true });
  }
}
setLen("L085", 0.085);
setLen("L100", 0.1);
setLen("L110", 0.11);
setLen("L340", 0.34);
setLen("L380", 0.38);
setLen("L500", 0.5);
run("save_document", { path: VENDOR });

for (const [name, cfg] of [
  ["frame_2020_bottom_left_100", "L100"],
  ["frame_2020_bottom_right_100", "L100"],
  ["frame_2020_top_left_100", "L100"],
  ["frame_2020_top_right_100", "L100"],
  ["frame_2020_bottom_front_110", "L110"],
  ["frame_2020_bottom_rear_110", "L110"],
  ["frame_2020_top_front_110", "L110"],
  ["frame_2020_top_rear_110", "L110"],
  ["frame_2020_vertical_back_left_340", "L340"],
  ["frame_2020_vertical_back_right_340", "L340"],
  ["frame_2020_vertical_front_left_340", "L340"],
  ["frame_2020_vertical_front_right_340", "L340"],
]) {
  run("set_component_configuration", { path: FRAME, component_name: name, configuration: cfg });
}

run("set_component_visible", { path: FRAME, component_name: LAYOUT, visible: true });

const ice = {
  corner_posts: iceBox("corner_posts"),
  bottom_rail_left: iceBox("bottom_rail_left"),
  bottom_rail_right: iceBox("bottom_rail_right"),
  bottom_rail_front: iceBox("bottom_rail_front"),
  bottom_rail_back: iceBox("bottom_rail_back"),
  top_rail_left: iceBox("top_rail_left"),
  top_rail_right: iceBox("top_rail_right"),
  top_rail_front: iceBox("top_rail_front"),
  top_rail_back: iceBox("top_rail_back"),
};

const T = 0.02;
const yPost = (ice.corner_posts[1] + ice.corner_posts[4]) / 2;
const len340 = ice.corner_posts[4] - ice.corner_posts[1];

const R_Z_to_Y = [1, 0, 0, 0, 0, 1, 0, 1, 0];
const R_Z_to_Z = [1, 0, 0, 0, 1, 0, 0, 0, 1];
const R_Z_to_X = [0, 1, 0, 0, 0, 1, 1, 0, 0];

function matFor(center, len, R) {
  const localCenter = [T / 2, T / 2, len / 2];
  const rlc = [
    R[0] * localCenter[0] + R[3] * localCenter[1] + R[6] * localCenter[2],
    R[1] * localCenter[0] + R[4] * localCenter[1] + R[7] * localCenter[2],
    R[2] * localCenter[0] + R[5] * localCenter[1] + R[8] * localCenter[2],
  ];
  return [
    R[0], R[1], R[2],
    R[3], R[4], R[5],
    R[6], R[7], R[8],
    center[0] - rlc[0],
    center[1] - rlc[1],
    center[2] - rlc[2],
    1, 0, 0, 0,
  ];
}

const railPlacements = [
  ["frame_2020_bottom_left_100", center(ice.bottom_rail_left), 0.1, R_Z_to_Z],
  ["frame_2020_bottom_right_100", center(ice.bottom_rail_right), 0.1, R_Z_to_Z],
  ["frame_2020_top_left_100", center(ice.top_rail_left), 0.1, R_Z_to_Z],
  ["frame_2020_top_right_100", center(ice.top_rail_right), 0.1, R_Z_to_Z],
  ["frame_2020_bottom_front_110", center(ice.bottom_rail_front), 0.11, R_Z_to_X],
  ["frame_2020_bottom_rear_110", center(ice.bottom_rail_back), 0.11, R_Z_to_X],
  ["frame_2020_top_front_110", center(ice.top_rail_front), 0.11, R_Z_to_X],
  ["frame_2020_top_rear_110", center(ice.top_rail_back), 0.11, R_Z_to_X],
  [
    "frame_2020_vertical_back_left_340",
    [center(ice.bottom_rail_left)[0], yPost, center(ice.bottom_rail_back)[2]],
    len340,
    R_Z_to_Y,
  ],
  [
    "frame_2020_vertical_back_right_340",
    [center(ice.bottom_rail_right)[0], yPost, center(ice.bottom_rail_back)[2]],
    len340,
    R_Z_to_Y,
  ],
  [
    "frame_2020_vertical_front_left_340",
    [center(ice.bottom_rail_left)[0], yPost, center(ice.bottom_rail_front)[2]],
    len340,
    R_Z_to_Y,
  ],
  [
    "frame_2020_vertical_front_right_340",
    [center(ice.bottom_rail_right)[0], yPost, center(ice.bottom_rail_front)[2]],
    len340,
    R_Z_to_Y,
  ],
];

for (const [name, ctr, len, R] of railPlacements) {
  run("set_component_transform", {
    path: FRAME,
    component_name: name,
    matrix: matFor(ctr, len, R),
    fix: true,
  });
}

// --- inside corner brackets (2 per corner × 8 corners) ---
const bracketBox = run("measure", { path: BRACKET_PART }).boundingBox;
const bracketDepthZ = bracketBox[5] - bracketBox[2];

const innerX = {
  left: ice.bottom_rail_left[3],
  right: ice.bottom_rail_right[0] - (bracketBox[3] - bracketBox[0]),
};
const innerZ = {
  back: ice.bottom_rail_back[2] - bracketDepthZ,
  front: ice.bottom_rail_front[5] - (bracketBox[5] - bracketBox[2]),
};

const R_YZ = [1, 0, 0, 0, 1, 0, 0, 0, 1];
const R_XY = [0, 0, 1, 0, 1, 0, 1, 0, 0];

function transformedBBox(R, t = [0, 0, 0]) {
  const [minX, minY, minZ, maxX, maxY, maxZ] = bracketBox;
  const corners = [
    [minX, minY, minZ],
    [maxX, minY, minZ],
    [minX, maxY, minZ],
    [maxX, maxY, minZ],
    [minX, minY, maxZ],
    [maxX, minY, maxZ],
    [minX, maxY, maxZ],
    [maxX, maxY, maxZ],
  ];
  const pts = corners.map(([x, y, z]) => [
    R[0] * x + R[1] * y + R[2] * z + t[0],
    R[3] * x + R[4] * y + R[5] * z + t[1],
    R[6] * x + R[7] * y + R[8] * z + t[2],
  ]);
  return [
    Math.min(...pts.map((p) => p[0])),
    Math.min(...pts.map((p) => p[1])),
    Math.min(...pts.map((p) => p[2])),
    Math.max(...pts.map((p) => p[0])),
    Math.max(...pts.map((p) => p[1])),
    Math.max(...pts.map((p) => p[2])),
  ];
}

function matrixForDesiredMin(R, desiredMin) {
  const b0 = transformedBBox(R);
  const t = [desiredMin[0] - b0[0], desiredMin[1] - b0[1], desiredMin[2] - b0[2]];
  return [R[0], R[1], R[2], R[3], R[4], R[5], R[6], R[7], R[8], t[0], t[1], t[2], 1, 0, 0, 0];
}

const corners = [];
for (const level of ["bottom", "top"]) {
  const yMin = level === "bottom" ? ice.bottom_rail_left[1] : ice.top_rail_left[1];
  for (const side of ["left", "right"]) {
    for (const depth of ["back", "front"]) {
      corners.push({ level, side, depth, yMin });
    }
  }
}

function sideBracketMin(corner) {
  const xMin = corner.side === "left" ? innerX.left : innerX.right;
  const zMin = corner.depth === "back" ? innerZ.back : innerZ.front;
  return [xMin, corner.yMin, zMin];
}

function faceBracketMin(corner) {
  const xMin = corner.side === "left" ? innerX.left : innerX.right - 0.01;
  const zMin = corner.depth === "back" ? innerZ.back : innerZ.front;
  return [xMin, corner.yMin, zMin];
}

const brackets = run("list_components", { path: FRAME })
  .components.map((c) => c.name)
  .filter((n) => n.startsWith("bracket_2028_corner"))
  .sort((a, b) => Number(a.match(/-(\d+)$/)?.[1] ?? 0) - Number(b.match(/-(\d+)$/)?.[1] ?? 0));

if (brackets.length !== 16) throw new Error(`Expected 16 brackets, found ${brackets.length}`);

const bracketPlacements = [];
let i = 0;
for (const corner of corners) {
  bracketPlacements.push({
    name: brackets[i++],
    corner,
    role: "side-rail",
    R: R_YZ,
    min: sideBracketMin(corner),
  });
  bracketPlacements.push({
    name: brackets[i++],
    corner,
    role: "face-rail",
    R: R_XY,
    min: faceBracketMin(corner),
  });
}

for (const p of bracketPlacements) {
  run("set_component_transform", {
    path: FRAME,
    component_name: p.name,
    matrix: matrixForDesiredMin(p.R, p.min),
    fix: true,
  });
}

run("set_component_visible", { path: FRAME, component_name: LAYOUT, visible: false });
run("save_document", { path: FRAME });

const railChecks = railPlacements.map(([name]) => {
  const b = run("get_component_box", { path: FRAME, component_name: name }).boundingBox;
  return {
    name,
    centerMm: [(b[0] + b[3]) / 2, (b[1] + b[4]) / 2, (b[2] + b[5]) / 2].map((v) => Math.round(v * 1000)),
    dimsMm: [b[3] - b[0], b[4] - b[1], b[5] - b[2]].map((v) => Math.round(v * 1000)),
  };
});

const layoutTargets = {
  yPostMm: Math.round(yPost * 1000),
  postSpanMm: Math.round(len340 * 1000),
  bottomRailYMm: Math.round(center(ice.bottom_rail_left)[1] * 1000),
  topRailYMm: Math.round(center(ice.top_rail_left)[1] * 1000),
};

console.log(JSON.stringify({ ok: true, layoutTargets, railChecks, bracketCount: bracketPlacements.length }, null, 2));
