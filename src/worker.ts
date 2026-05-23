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
  | "set_component_configuration"
  | "mate_component_origin"
  | "list_configurations"
  | "add_configuration_copy"
  | "get_component_box"
  | "transform_component"
  | "set_dimension"
  | "mate_planes"
  | "get_feature_box"
  | "mate_coincident"
  | "mate_parallel"
  | "align_component_to_feature"
  | "probe_feature_faces"
  | "torso_frame_build_mates"
  | "save_document"
  | "set_custom_properties"
  | "replace_components_by_path";

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
