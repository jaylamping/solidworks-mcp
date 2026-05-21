import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const workerProject = path.join(root, "workers/SolidWorksComWorker");
const TORSO_ASM = "C:/code/marengo/hardware/cad/assemblies/marengo_torso_asm_revA.SLDASM";

function runWorker(command, args) {
  const payload = JSON.stringify({ command, args });
  const result = spawnSync("dotnet", ["run", "--project", workerProject, "--no-launch-profile"], {
    cwd: root,
    input: payload,
    encoding: "utf8",
    windowsHide: true,
  });
  const parsed = JSON.parse(result.stdout.trim());
  if (!parsed.ok) throw new Error(parsed.error);
  return parsed.data;
}

runWorker("set_component_fixed", {
  path: TORSO_ASM,
  component_name: "marengo_torso_layout_revA-1",
  fixed: true,
});
runWorker("save_document", { path: TORSO_ASM });
console.log("refixed layout anchor");
