import { runWorker } from "./lib/worker-client.mjs";




const asm = "C:/code/marengo/cad/assemblies/marengo_torso_asm.SLDASM";
const vendor = "C:/code/marengo/cad/vendor/vendor_robstride_rs03_vendor.SLDPRT";

console.log(
  JSON.stringify(
    {
      vendorFeatures: runWorker("list_features", { path: vendor }),
      vendorMeasure: runWorker("measure", { path: vendor }),
      motorBox: runWorker("get_component_box", {
        path: asm,
        component_name: "actuator_rs03_left_shoulder_pitch",
      }),
    },
    null,
    2,
  ),
);