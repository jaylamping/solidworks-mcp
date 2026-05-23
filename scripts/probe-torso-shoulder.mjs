import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const workerProject = path.join(root, "workers/SolidWorksComWorker");

function runWorker(command, args) {
  const payload = JSON.stringify({ command, args });
  const result = spawnSync(
    "dotnet",
    ["run", "--project", workerProject, "--no-launch-profile"],
    { cwd: root, input: payload, encoding: "utf8", windowsHide: true },
  );
  if (result.status !== 0) {
    throw new Error(result.stderr || result.stdout);
  }
  return JSON.parse(result.stdout.trim()).data;
}

const paths = {
  vendor: "C:/code/marengo/hardware/cad/vendor/vendor_robstride_rs03_vendor.SLDPRT",
  layout: "C:/code/marengo/hardware/cad/parts/marengo_torso_layout_revA.SLDPRT",
  asm: "C:/code/marengo/hardware/cad/assemblies/marengo_torso_asm_revA.SLDASM",
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
