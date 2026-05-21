/**
 * DESTRUCTIVE: deletes mates, modifies configs. Requires --confirm.
 */
import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

if (!process.argv.slice(2).includes("--confirm")) {
  console.error(
    "Refusing to run: torso-frame-layout-sync.mjs modifies assemblies in SolidWorks.\n" +
      "  node scripts/torso-frame-layout-sync.mjs --confirm",
  );
  process.exit(1);
}

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const workerProject = path.join(root, "workers/SolidWorksComWorker");

const FRAME_ASM = "C:/code/marengo/hardware/cad/assemblies/marengo_torso_frame_asm_revA.SLDASM";
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
const depthRails = [
  "frame_2020_bottom_left_085",
  "frame_2020_bottom_right_085",
  "frame_2020_top_left_085",
  "frame_2020_top_right_085",
];

try {
  steps.push({
    step: "show_layout_jig",
    result: runWorker("set_component_visible", {
      path: FRAME_ASM,
      component_name: "marengo_torso_layout_revA",
      visible: true,
    }),
  });

  steps.push({
    step: "delete_all_mates",
    result: runWorker("delete_all_mates", { path: FRAME_ASM }),
  });

  steps.push({
    step: "fix_layout_jig",
    result: runWorker("set_component_fixed", {
      path: FRAME_ASM,
      component_name: "marengo_torso_layout_revA",
      fixed: true,
    }),
  });

  steps.push({
    step: "mate_layout_to_origin",
    result: runWorker("mate_component_origin", {
      path: FRAME_ASM,
      component_name: "marengo_torso_layout_revA",
      reference: "urdf_link_frame",
    }),
  });

  for (const from of depthRails) {
    const to = from.replace("_085", "_100");
    steps.push({
      step: `config_${from}`,
      result: runWorker("set_component_configuration", {
        path: FRAME_ASM,
        component_name: from,
        configuration: "L100",
      }),
    });
    steps.push({
      step: `rename_${from}`,
      result: runWorker("rename_component", { path: FRAME_ASM, from, to }),
    });
  }

  steps.push({
    step: "hide_layout_jig",
    result: runWorker("set_component_visible", {
      path: FRAME_ASM,
      component_name: "marengo_torso_layout_revA",
      visible: false,
    }),
  });

  steps.push({
    step: "save_frame_asm",
    result: runWorker("save_document", { path: FRAME_ASM }),
  });

  steps.push({
    step: "mate_torso_frame_to_layout",
    result: runWorker("mate_coord_sys", {
      path: TORSO_ASM,
      component_1: "marengo_torso_layout_revA-1",
      ref_1: "urdf_link_frame",
      component_2: "marengo_torso_layout_revA-2",
      ref_2: "urdf_link_frame",
    }),
  });

  steps.push({
    step: "fix_torso_layout",
    result: runWorker("set_component_fixed", {
      path: TORSO_ASM,
      component_name: "marengo_torso_layout_revA-1",
      fixed: true,
    }),
  });

  steps.push({
    step: "save_torso_asm",
    result: runWorker("save_document", { path: TORSO_ASM }),
  });

  console.log(JSON.stringify({ ok: true, steps }, null, 2));
} catch (error) {
  console.log(
    JSON.stringify(
      { ok: false, steps, error: error instanceof Error ? error.message : String(error) },
      null,
      2,
    ),
  );
  process.exit(1);
}
