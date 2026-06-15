import { runWorker } from "./lib/worker-client.mjs";




const paths = {
  vendor: "C:/code/marengo/hardware/cad/vendor/vendor_robstride_rs03_vendor.SLDPRT",
  layout: "C:/code/marengo/hardware/cad/parts/marengo_torso_layout.SLDPRT",
  asm: "C:/code/marengo/hardware/cad/assemblies/marengo_torso_asm.SLDASM",
};

console.log(
  JSON.stringify(
    {
      vendorRefs: runWorker("list_reference_geometry", { path: paths.vendor }),
      layoutRefs: runWorker("list_reference_geometry", { path: paths.layout }),
      components: runWorker("list_components", { path: paths.asm }),
      mates: runWorker("list_mates", { path: paths.asm }),
    },
    null,
    2,
  ),
);