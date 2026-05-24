/**
 * DESTRUCTIVE: same class as layout-place (worker torso_frame_build_mates). Requires --confirm.
 */
import { runWorker, requireConfirm } from "./lib/worker-client.mjs";

requireConfirm(
  process.argv.slice(2),
  "node scripts/torso-frame-autobuild.mjs --confirm",
);

const FRAME_ASM = "C:/code/marengo/hardware/cad/assemblies/marengo_torso_frame_asm_revA.SLDASM";

const data = runWorker("torso_frame_build_mates", {
  path: FRAME_ASM,
  include_brackets: true,
  rebuild_configs: true,
});

console.log(JSON.stringify(data, null, 2));