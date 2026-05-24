/**
 * Rebuild marengo_torso_compute_shelf_upper_revA from live shoulder actuator geometry.
 * Requires SolidWorks running with marengo_torso_asm_revA open or on disk.
 *
 * Usage:
 *   node scripts/build-compute-shelf.mjs
 */
import { runWorker } from "./lib/worker-client.mjs";



const ASM =
  "C:/code/marengo/hardware/cad/assemblies/marengo_torso_asm_revA.SLDASM";
const PART =
  "C:/code/marengo/hardware/cad/parts/marengo_torso_compute_shelf_upper_revA.SLDPRT";


const result = runWorker("build_torso_compute_shelf", {
  path: ASM,
  part_path: PART,
  save: true,
  mate_in_assembly: true,
});

console.log(JSON.stringify(result, null, 2));