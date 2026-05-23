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
  if (result.status !== 0) throw new Error(result.stderr || result.stdout);
  return JSON.parse(result.stdout.trim()).data;
}

const asm = "C:/code/marengo/hardware/cad/assemblies/marengo_torso_asm_revA.SLDASM";
const vendor = "C:/code/marengo/hardware/cad/vendor/vendor_robstride_rs03_vendor.SLDPRT";

console.log(
  JSON.stringify(
    {
      vendorFeatures: runWorker("list_features", { path: vendor }),
      vendorMeasure: runWorker("measure", { path: vendor }),
      motorBox: runWorker("get_component_box", {
        path: asm,
        component_name: "actuator_rs03_left_shoulder_roll",
      }),
    },
    null,
    2,
  ),
);
