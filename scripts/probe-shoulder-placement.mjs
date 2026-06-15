import { runWorker } from "./lib/worker-client.mjs";




const asm = "C:/code/marengo/hardware/cad/assemblies/marengo_torso_asm.SLDASM";

const components = runWorker("list_components", { path: asm });
const actuators = components.components.filter((c) =>
  String(c.name).includes("shoulder_roll"),
);

const boxes = {};
for (const a of actuators) {
  const prefix = a.name.includes("left") ? "left" : "right";
  boxes[prefix] = runWorker("get_component_box", {
    path: asm,
    component_name: a.name,
  });
}

const layoutBox = runWorker("get_feature_box", {
  path: asm,
  component_name: "marengo_torso_layout",
  feature_name: "torso_inner_clear",
}).catch?.(() => null);

let innerClear = null;
try {
  innerClear = runWorker("get_feature_box", {
    path: asm,
    component_name: "marengo_torso_frame_asm/marengo_torso_layout",
    feature_name: "torso_inner_clear",
  });
} catch {
  try {
    innerClear = runWorker("get_feature_box", {
      path: asm,
      component_name: "marengo_torso_layout",
      feature_name: "torso_inner_clear",
    });
  } catch {
    innerClear = null;
  }
}

console.log(
  JSON.stringify(
    {
      actuators: actuators.map((a) => ({
        name: a.name,
        isFixed: a.isFixed,
        visible: a.visible,
      })),
      motorBoxes: boxes,
      innerClearBox: innerClear,
      mateCount: runWorker("list_mates", { path: asm }).mateCount,
    },
    null,
    2,
  ),
);