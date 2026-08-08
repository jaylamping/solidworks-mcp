import { runWorker } from "../lib/worker-client.mjs";

const asm =
  process.env.MARENGO_ARM_RIGHT_ASM ??
  "\\\\wsl$\\Ubuntu\\home\\joey\\code\\marengo\\cad\\assemblies\\marengo_arm_right_asm.SLDASM";
const mateName = process.env.SHOULDER_ROLL_LIMIT_MATE ?? "LimitAngle19";
const roll = "marengo_shoulder_roll_bracket_right-3";

console.log("=== checkpoint ===");
console.log(
  JSON.stringify(
    runWorker("checkpoint_document", { path: asm, force: true }),
    null,
    2,
  ),
);

const beforeMate = runWorker("list_mates", { path: asm }).mates.find(
  (m) => m.name === mateName,
);
const beforeXform = runWorker("get_component_transform", {
  path: asm,
  component_name: roll,
});
console.log("before mate", beforeMate);
console.log("before roll matrix", beforeXform.matrix);

const angleDeg =
  typeof beforeMate?.angleDeg === "number" ? beforeMate.angleDeg : 4.2749212344057925;

console.log("=== set_mate_limit_angle unflip ===");
const updated = runWorker("set_mate_limit_angle", {
  path: asm,
  mate_name: mateName,
  min_angle_deg: -5.55,
  max_angle_deg: 184.97,
  angle_deg: angleDeg,
  flip_dimension: false,
});
console.log(JSON.stringify(updated, null, 2));

const afterMate = runWorker("list_mates", { path: asm }).mates.find(
  (m) => m.name === mateName,
);
const afterXform = runWorker("get_component_transform", {
  path: asm,
  component_name: roll,
});
console.log("after mate", afterMate);
console.log(
  "pose stable?",
  JSON.stringify(beforeXform.matrix) === JSON.stringify(afterXform.matrix),
);
