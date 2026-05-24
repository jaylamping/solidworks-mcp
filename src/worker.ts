import { spawn } from "node:child_process";
import fs from "node:fs";

import { packageRoot, workerDllPath, workerProjectPath } from "./config.js";

export type WorkerCommand =
  | "status"
  | "open"
  | "export"
  | "measure"
  | "list_features"
  | "inspect_document"
  | "list_components"
  | "list_reference_geometry"
  | "list_bom"
  | "list_mates"
  | "set_component_visible"
  | "set_component_fixed"
  | "rename_component"
  | "mate_coord_sys"
  | "delete_all_mates"
  | "delete_mates_in_range"
  | "delete_mate"
  | "unfix_all_components"
  | "set_component_configuration"
  | "mate_component_origin"
  | "list_configurations"
  | "list_dimensions"
  | "set_feature_suppression"
  | "add_configuration_copy"
  | "get_component_box"
  | "transform_component"
  | "set_component_transform"
  | "set_dimension"
  | "mate_planes"
  | "get_feature_box"
  | "get_part_feature_box"
  | "mate_coincident"
  | "mate_parallel"
  | "mate_try_coincident"
  | "mate_try_parallel"
  | "mate_try_distance"
  | "mate_try_perpendicular"
  | "mate_try_width"
  | "mate_distance"
  | "mate_perpendicular"
  | "mate_width"
  | "mate_probe"
  | "align_component_to_feature"
  | "probe_feature_faces"
  | "get_planar_face_index"
  | "select_face_by_ray"
  | "get_persist_reference"
  | "select_by_persist_reference"
  | "rebuild_document"
  | "ensure_offset_plane"
  | "reset_component_transform"
  | "insert_coord_sys"
  | "list_interferences"
  | "set_mate_suppression"
  | "torso_frame_build_mates"
  | "save_document"
  | "close_all_documents"
  | "diagnose_part_save"
  | "clone_solid_body_part"
  | "mirror_part_file"
  | "make_component_independent"
  | "set_custom_properties"
  | "replace_components_by_path"
  | "replace_component_path"
  | "layout_add_shoulder_mounts"
  | "place_shoulder_roll_motors"
  | "get_component_transform"
  | "capture_shoulder_roll_golden"
  | "apply_shoulder_roll_golden"
  | "vendor_add_rs03_urdf_frame"
  | "insert_component"
  | "cut_actuator_cavity"
  | "build_torso_compute_shelf"
  | "debug_mate_entities";

export interface WorkerRequest {
  command: WorkerCommand;
  args?: Record<string, unknown>;
}

interface WorkerResponse {
  ok: boolean;
  data?: unknown;
  error?: string;
}

/** Serialize COM calls — SolidWorks is STA; concurrent workers crash it. */
let workerQueue: Promise<unknown> = Promise.resolve();

export async function runWorker(request: WorkerRequest): Promise<unknown> {
  const run = workerQueue.then(() => spawnWorkerOnce(request));
  workerQueue = run.then(
    () => undefined,
    () => undefined,
  );
  return run;
}

async function spawnWorkerOnce(request: WorkerRequest): Promise<unknown> {
  const dll = workerDllPath();
  const useDll = fs.existsSync(dll);
  const child = spawn(
    "dotnet",
    useDll ? ["exec", dll] : ["run", "--project", workerProjectPath(), "--no-launch-profile"],
    {
      cwd: packageRoot(),
      stdio: ["pipe", "pipe", "pipe"],
      windowsHide: true,
    },
  );

  const stdout: Buffer[] = [];
  const stderr: Buffer[] = [];

  child.stdout.on("data", (chunk: Buffer) => stdout.push(chunk));
  child.stderr.on("data", (chunk: Buffer) => stderr.push(chunk));
  child.stdin.end(`${JSON.stringify(request)}\n`);

  const code = await new Promise<number | null>((resolve) => {
    child.on("close", resolve);
  });

  const out = Buffer.concat(stdout).toString("utf8").trim();
  const err = Buffer.concat(stderr).toString("utf8").trim();

  if (code !== 0) {
    throw new Error(`SolidWorks worker exited ${code}${err ? `: ${err}` : ""}`);
  }

  let parsed: WorkerResponse;
  try {
    parsed = JSON.parse(out) as WorkerResponse;
  } catch (error) {
    throw new Error(`SolidWorks worker returned non-JSON output: ${out || err || String(error)}`);
  }

  if (!parsed.ok) {
    throw new Error(parsed.error ?? "SolidWorks worker failed");
  }

  return parsed.data ?? {};
}
