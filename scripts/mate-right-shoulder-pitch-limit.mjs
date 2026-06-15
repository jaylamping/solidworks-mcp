/**
 * Limit-angle mate for right shoulder pitch in marengo_torso_asm.
 * Bench envelope: -50 deg .. +180 deg.
 */
import { runWorker } from "./lib/worker-client.mjs";

const TORSO = "C:/code/marengo/hardware/cad/assemblies/marengo_torso_asm.SLDASM";

try {
  runWorker("open", { path: TORSO });

  // Remove accidental parallel mate from earlier probe if present.
  try {
    runWorker("delete_mate", { path: TORSO, mate_name: "Parallel16" });
  } catch {
    // Mate may already be absent.
  }

  const result = runWorker("mate_limit_angle", {
    path: TORSO,
    component_1: "actuator_rs03_right_shoulder_pitch",
    ref_1: "Right Plane",
    component_2: "marengo_shoulder_roll_bracket_right-1",
    ref_2: "Front Plane",
    component_axis: "actuator_rs03_right_shoulder_pitch",
    ref_axis: "shaft_axis",
    min_deg: -50,
    max_deg: 180,
    save: true,
  });

  console.log(JSON.stringify({ ok: true, result }, null, 2));
} catch (error) {
  console.log(
    JSON.stringify(
      { ok: false, error: error instanceof Error ? error.message : String(error) },
      null,
      2,
    ),
  );
  process.exit(1);
}
