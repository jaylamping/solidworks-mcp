import { runWorker } from "./lib/worker-client.mjs";



const TORSO_ASM =
  "C:/code/marengo/hardware/cad/assemblies/marengo_torso_asm.SLDASM";


const golden = JSON.parse(readFileSync(goldenPath, "utf8"));

const result = runWorker("apply_shoulder_roll_golden", {
  path: TORSO_ASM,
  left: golden.left,
  right: golden.right,
  save: true,
  fix: true,
});

console.log(JSON.stringify({ ok: true, goldenPath, result }, null, 2));