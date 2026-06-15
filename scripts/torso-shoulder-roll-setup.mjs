import { runWorker } from "./lib/worker-client.mjs";



const LAYOUT_PART =
  "C:/code/marengo/hardware/cad/parts/marengo_torso_layout.SLDPRT";
const TORSO_ASM =
  "C:/code/marengo/hardware/cad/assemblies/marengo_torso_asm.SLDASM";

const INNER_RAIL_MM = 55;


const steps = [];

try {
  steps.push({
    step: "layout_shoulder_mounts",
    result: runWorker("layout_add_shoulder_mounts", {
      path: LAYOUT_PART,
      save: true,
      replace_existing: true,
      inner_rail_mm: INNER_RAIL_MM,
      depth_offset_mm: 0,
    }),
  });

  if (existsSync(goldenPath)) {
    const golden = JSON.parse(readFileSync(goldenPath, "utf8"));
    steps.push({
      step: "apply_shoulder_roll_golden",
      result: runWorker("apply_shoulder_roll_golden", {
        path: TORSO_ASM,
        left: golden.left,
        right: golden.right,
        save: true,
        fix: true,
      }),
    });
  } else {
    steps.push({
      step: "place_shoulder_roll_motors",
      result: runWorker("place_shoulder_roll_motors", {
        path: TORSO_ASM,
        save: true,
        inner_rail_mm: INNER_RAIL_MM,
        inner_flange_mm: 40.3975,
      }),
    });
  }

  console.log(JSON.stringify({ ok: true, steps }, null, 2));
} catch (error) {
  console.log(
    JSON.stringify(
      {
        ok: false,
        steps,
        error: error instanceof Error ? error.message : String(error),
      },
      null,
      2,
    ),
  );
  process.exit(1);
}