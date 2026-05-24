import { runWorker } from "./lib/worker-client.mjs";



const TORSO_ASM =
  "C:/code/marengo/hardware/cad/assemblies/marengo_torso_asm_revA.SLDASM";


const steps = [];

try {
  const existing = runWorker("list_components", { path: TORSO_ASM });
  const hasRight = existing.components?.some((c) =>
    String(c.name).startsWith("actuator_rs03_right_shoulder_roll"),
  );
  const vendorRight = existing.components?.find(
    (c) =>
      String(c.name).startsWith("vendor_robstride_rs03_vendor") &&
      !String(c.name).includes("left"),
  );

  if (!hasRight && vendorRight) {
    steps.push({
      step: "rename_right_shoulder_roll",
      result: runWorker("rename_component", {
        path: TORSO_ASM,
        from: vendorRight.name.replace(/-\d+$/, ""),
        to: "actuator_rs03_right_shoulder_roll",
        save: true,
      }),
    });
  }

  const captured = runWorker("capture_shoulder_roll_golden", {
    path: TORSO_ASM,
    left_component: "actuator_rs03_left_shoulder_roll",
    right_component: "actuator_rs03_right_shoulder_roll",
  });

  const golden = {
    ...captured,
    left: {
      componentPrefix: "actuator_rs03_left_shoulder_roll",
      matrix: captured.left.matrix,
      boundingBoxM: captured.left.boundingBoxM,
    },
    right: {
      componentPrefix: "actuator_rs03_right_shoulder_roll",
      matrix: captured.right.matrix,
      boundingBoxM: captured.right.boundingBoxM,
    },
  };

  fs.writeFileSync(goldenPath, `${JSON.stringify(golden, null, 2)}\n`, "utf8");
  steps.push({ step: "write_golden_json", path: goldenPath, captured: golden });

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