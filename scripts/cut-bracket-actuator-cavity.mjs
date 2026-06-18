import { runWorker } from "./lib/worker-client.mjs";



const TORSO_ASM =
  "C:/code/marengo/cad/assemblies/marengo_torso_asm.SLDASM";


const result = runWorker("cut_actuator_cavity", {
  path: TORSO_ASM,
  bracket_component: "marengo_shoulder_pitch_mount_bracket_right",
  tool_component: "actuator_rs03_right_shoulder_pitch",
  clearance_mm: 0.5,
  save: true,
});

console.log(JSON.stringify({ ok: true, result }, null, 2));