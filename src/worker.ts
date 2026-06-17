import { spawn, spawnSync, type ChildProcess } from "node:child_process";
import fs from "node:fs";

import { packageRoot, workerDllPath, workerProjectPath } from "./config.js";
import { parseWorkerError, SolidWorksWorkerError, type WorkerError } from "./errors.js";

/** All worker commands — kept in sync via `npm run check:registry`. */
export type WorkerCommand =
  | "activate_document"
  | "actuator_add_urdf_frame"
  | "actuator_cut_cavity"
  | "actuator_get_envelope"
  | "actuator_insert_vendor"
  | "actuator_list_models"
  | "actuator_mount_hole_pattern"
  | "actuator_probe_mount_face"
  | "add_configuration_copy"
  | "add_standard_views"
  | "align_component_to_feature"
  | "apply_shoulder_roll_golden"
  | "assembly_diagnostics"
  | "batch_invoke"
  | "build_torso_compute_shelf"
  | "capture_shoulder_roll_golden"
  | "checkpoint_document"
  | "clone_solid_body_part"
  | "close_all_documents"
  | "close_document"
  | "component_mass_properties"
  | "copy_with_mates"
  | "create_drawing_from_model"
  | "create_mallet_mount"
  | "create_sketch"
  | "create_subassembly"
  | "cut_actuator_cavity"
  | "debug_mate_entities"
  | "delete_all_mates"
  | "delete_feature"
  | "delete_mate"
  | "delete_mates_in_range"
  | "diagnose_com"
  | "diagnose_document"
  | "diagnose_part_save"
  | "diagnose_selection"
  | "dissolve_component"
  | "ensure_offset_plane"
  | "explain_error"
  | "explode_view"
  | "export"
  | "export_link_transforms"
  | "feature_chamfer"
  | "feature_circular_pattern"
  | "feature_extrude_boss"
  | "feature_extrude_cut"
  | "feature_fillet"
  | "feature_linear_pattern"
  | "feature_mirror"
  | "get_assembly_degrees_of_freedom"
  | "get_component_box"
  | "get_component_references"
  | "get_component_transform"
  | "get_equations"
  | "get_feature_box"
  | "get_mass_properties"
  | "get_material"
  | "get_open_documents"
  | "get_part_feature_box"
  | "get_persist_reference"
  | "get_planar_face_index"
  | "get_selection"
  | "import_step"
  | "insert_component"
  | "insert_coord_sys"
  | "inspect_document"
  | "invoke"
  | "layout_add_shoulder_mounts"
  | "list_bodies"
  | "list_bom"
  | "list_broken_references"
  | "list_components"
  | "list_configurations"
  | "list_dimensions"
  | "list_display_states"
  | "list_features"
  | "list_interferences"
  | "list_mates"
  | "list_reference_geometry"
  | "list_sheet_views"
  | "list_sketches"
  | "make_component_independent"
  | "mate_coincident"
  | "mate_component_origin"
  | "mate_coord_sys"
  | "mate_distance"
  | "mate_limit_angle"
  | "mate_parallel"
  | "mate_perpendicular"
  | "mate_planes"
  | "mate_probe"
  | "mate_record_macro"
  | "mate_replay_sequence"
  | "mate_try_coincident"
  | "mate_try_distance"
  | "mate_try_parallel"
  | "mate_try_perpendicular"
  | "mate_try_width"
  | "mate_width"
  | "measure"
  | "measure_distance"
  | "mirror_component"
  | "mirror_part_file"
  | "new_document"
  | "open"
  | "pack_and_go"
  | "place_shoulder_roll_motors"
  | "probe_feature_faces"
  | "rebuild_document"
  | "rename_component"
  | "replace_component_path"
  | "replace_components_by_path"
  | "reset_component_transform"
  | "resolve_lightweight"
  | "resolve_selection"
  | "save_document"
  | "select_by_persist_reference"
  | "select_face_by_ray"
  | "set_component_configuration"
  | "set_component_fixed"
  | "set_component_transform"
  | "set_component_visible"
  | "set_custom_properties"
  | "set_dimension"
  | "set_feature_suppression"
  | "set_mate_suppression"
  | "set_material"
  | "sketch_circle"
  | "sketch_exit"
  | "sketch_line"
  | "sketch_rectangle"
  | "status"
  | "torso_frame_build_mates"
  | "transform_component"
  | "unfix_all_components"
  | "vendor_add_rs03_urdf_frame";

export interface WorkerRequest {
  command: WorkerCommand;
  args?: Record<string, unknown>;
}

export type WorkerResponse<T = unknown> =
  | { ok: true; data: T }
  | { ok: false; error: WorkerError | string };

/** Serialize COM calls — SolidWorks is STA; concurrent workers crash it. */
let workerQueue: Promise<unknown> = Promise.resolve();

let persistentChild: ChildProcess | null = null;

/**
 * Optional persistent worker mode: set SOLIDWORKS_MCP_PERSISTENT_WORKER=1 to keep one
 * long-lived dotnet process (reduces spawn overhead). Default remains ephemeral per call.
 */
const usePersistentWorker = process.env.SOLIDWORKS_MCP_PERSISTENT_WORKER === "1";

export async function runWorker(request: WorkerRequest): Promise<unknown> {
  const run = workerQueue.then(() =>
    usePersistentWorker ? persistentWorkerOnce(request) : spawnWorkerOnce(request),
  );
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

  return collectWorkerResponse(child, request);
}

async function persistentWorkerOnce(request: WorkerRequest): Promise<unknown> {
  if (!persistentChild || persistentChild.killed) {
    const dll = workerDllPath();
    const useDll = fs.existsSync(dll);
    persistentChild = spawn(
      "dotnet",
      useDll ? ["exec", dll] : ["run", "--project", workerProjectPath(), "--no-launch-profile"],
      {
        cwd: packageRoot(),
        stdio: ["pipe", "pipe", "pipe"],
        windowsHide: true,
      },
    );
  }

  return collectWorkerResponse(persistentChild, request);
}

async function collectWorkerResponse(
  child: ChildProcess,
  request: WorkerRequest,
): Promise<unknown> {
  const stdout: Buffer[] = [];
  const stderr: Buffer[] = [];

  child.stdout?.on("data", (chunk: Buffer) => stdout.push(chunk));
  child.stderr?.on("data", (chunk: Buffer) => stderr.push(chunk));
  child.stdin?.end(`${JSON.stringify(request)}\n`);

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
    const envelope =
      typeof parsed.error === "string"
        ? ({ code: "WORKER_ERROR", message: parsed.error, category: "worker" as const } satisfies WorkerError)
        : parseWorkerError(parsed.error);
    if (envelope) {
      throw new SolidWorksWorkerError(envelope);
    }
    throw new Error(typeof parsed.error === "string" ? parsed.error : "SolidWorks worker failed");
  }

  return parsed.data ?? {};
}

/** Synchronous worker spawn for scripts (no queue). */
export function runWorkerSync(request: WorkerRequest): unknown {
  const dll = workerDllPath();
  const useDll = fs.existsSync(dll);
  const result = spawnSync(
    "dotnet",
    useDll ? ["exec", dll] : ["run", "--project", workerProjectPath(), "--no-launch-profile"],
    {
      cwd: packageRoot(),
      input: `${JSON.stringify(request)}\n`,
      encoding: "utf8",
      windowsHide: true,
    },
  );

  const output = (result.stdout || "").trim();
  if (result.status !== 0 && !output.includes('"ok"')) {
    throw new Error(result.stderr || output || `Worker exited ${result.status}`);
  }

  const parsed = JSON.parse(output) as WorkerResponse;
  if (!parsed.ok) {
    const envelope = typeof parsed.error === "object" ? parseWorkerError(parsed.error) : null;
    if (envelope) {
      throw new SolidWorksWorkerError(envelope);
    }
    throw new Error(typeof parsed.error === "string" ? parsed.error : "Worker failed");
  }

  return parsed.data ?? {};
}
