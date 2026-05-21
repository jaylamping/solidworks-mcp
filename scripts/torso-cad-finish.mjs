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

  if (result.status !== 0) {
    throw new Error(`Worker exited ${result.status}: ${result.stderr || result.stdout}`);
  }

  const parsed = JSON.parse(result.stdout.trim());
  if (!parsed.ok) {
    throw new Error(parsed.error ?? "Worker failed");
  }

  return parsed.data;
}

const steps = [];

try {
  steps.push({
    step: "unfix_layout",
    result: runWorker("set_component_fixed", {
      path: TORSO_ASM,
      component_name: "marengo_torso_layout_revA-1",
      fixed: false,
    }),
  });

  steps.push({
    step: "unfix_frame_subasm",
    result: runWorker("set_component_fixed", {
      path: TORSO_ASM,
      component_name: "marengo_torso_frame_asm_revA-1",
      fixed: false,
    }),
  });

  steps.push({
    step: "mate_frame_to_urdf_link_frame",
    result: runWorker("mate_coord_sys", {
      path: TORSO_ASM,
      component_1: "marengo_torso_layout_revA-1",
      ref_1: "urdf_link_frame",
      component_2: "marengo_torso_layout_revA-2",
      ref_2: "urdf_link_frame",
    }),
  });

  steps.push({
    step: "list_mates_before_save",
    result: runWorker("list_mates", { path: TORSO_ASM }),
  });

  steps.push({
    step: "save_torso_asm",
    result: runWorker("save_document", { path: TORSO_ASM }),
  });

  console.log(JSON.stringify({ ok: true, steps }, null, 2));
} catch (error) {
  console.log(JSON.stringify({ ok: false, steps, error: error instanceof Error ? error.message : String(error) }, null, 2));
  process.exit(1);
}
