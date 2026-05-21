import { spawn } from "node:child_process";

import { packageRoot, workerProjectPath } from "./config.js";

export type WorkerCommand =
  | "status"
  | "open"
  | "export"
  | "measure"
  | "list_features"
  | "inspect_document"
  | "list_components"
  | "list_reference_geometry"
  | "list_bom";

export interface WorkerRequest {
  command: WorkerCommand;
  args?: Record<string, unknown>;
}

interface WorkerResponse {
  ok: boolean;
  data?: unknown;
  error?: string;
}

export async function runWorker(request: WorkerRequest): Promise<unknown> {
  const child = spawn(
    "dotnet",
    ["run", "--project", workerProjectPath(), "--no-launch-profile"],
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
