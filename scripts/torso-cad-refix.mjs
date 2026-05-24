import { runWorker } from "./lib/worker-client.mjs";




runWorker("set_component_fixed", {
  path: TORSO_ASM,
  component_name: "marengo_torso_layout_revA-1",
  fixed: true,
});
runWorker("save_document", { path: TORSO_ASM });
console.log("refixed layout anchor");