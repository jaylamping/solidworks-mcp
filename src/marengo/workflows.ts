import { assertAllowedPath } from "../config.js";
import { runWorker } from "../worker.js";

export interface WorkflowOptions {
  path?: string;
  confirm?: true;
}

export async function runTorsoAsmBuildWorkflow(options: WorkflowOptions) {
  if (!options.path) {
    throw new Error("path is required");
  }
  if (options.confirm !== true) {
    throw new Error("marengo_workflow_torso_asm_build requires confirm: true");
  }

  const path = assertAllowedPath(options.path);
  const checkpoint = await runWorker({ command: "checkpoint_document", args: { path } });
  const frame = await runWorker({
    command: "torso_frame_build_mates",
    args: { path, confirm: true },
  });
  const mounts = await runWorker({
    command: "layout_add_shoulder_mounts",
    args: { path, confirm: true },
  });

  return { checkpoint, frame, mounts };
}

export async function runShoulderRollSetupWorkflow(options: WorkflowOptions) {
  if (!options.path) {
    throw new Error("path is required");
  }
  const path = assertAllowedPath(options.path);
  const motors = await runWorker({
    command: "place_shoulder_roll_motors",
    args: { path, confirm: options.confirm ?? true },
  });
  return { path, motors };
}

export async function runComputeShelfMateWorkflow(options: WorkflowOptions) {
  if (!options.path) {
    throw new Error("path is required");
  }
  const path = assertAllowedPath(options.path);
  const shelf = await runWorker({
    command: "build_torso_compute_shelf",
    args: { path, confirm: options.confirm ?? true, save: true },
  });
  return { path, shelf };
}

export async function runBilateralMirrorWorkflow(options: WorkflowOptions) {
  if (!options.path) {
    throw new Error("path is required");
  }
  const path = assertAllowedPath(options.path);
  return {
    path,
    stub: true,
    message: "Use mirror_part_file + replace_component_path scripts for full bilateral mirror today.",
    confirm: options.confirm ?? false,
  };
}

export async function runBracketRepairWorkflow(options: WorkflowOptions) {
  if (!options.path) {
    throw new Error("path is required");
  }
  if (options.confirm !== true) {
    throw new Error("marengo_workflow_bracket_repair requires confirm: true");
  }

  const path = assertAllowedPath(options.path);
  const checkpoint = await runWorker({ command: "checkpoint_document", args: { path } });
  const diagnose = await runWorker({ command: "diagnose_part_save", args: { path } });
  const save = await runWorker({ command: "save_document", args: { path } });

  return { checkpoint, diagnose, save, path };
}
