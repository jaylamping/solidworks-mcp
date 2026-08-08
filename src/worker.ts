import { spawn, spawnSync, type ChildProcess } from "node:child_process";
import fs from "node:fs";

import { sharedPersistentSession } from "./com-session.js";
import { packageRoot, workerDllPath, workerProjectPath } from "./config.js";
import { parseWorkerError, SolidWorksWorkerError, type WorkerError } from "./errors.js";
import type { WorkerCommand } from "./generated/worker-command.js";

export type { WorkerCommand } from "./generated/worker-command.js";

export interface WorkerRequest {
  command: WorkerCommand;
  args?: Record<string, unknown>;
}

export type WorkerResponse<T = unknown> =
  | { ok: true; data: T }
  | { ok: false; error: WorkerError | string };

/** Serialize COM calls — SolidWorks is STA; concurrent workers crash it. */
let workerQueue: Promise<unknown> = Promise.resolve();

/**
 * Optional persistent session worker. Set SOLIDWORKS_MCP_PERSISTENT_WORKER=1 or
 * SOLIDWORKS_MCP_WORKER_MODE=session. Default remains ephemeral oneshot per call.
 */
const usePersistentWorker =
  process.env.SOLIDWORKS_MCP_PERSISTENT_WORKER === "1"
  || process.env.SOLIDWORKS_MCP_WORKER_MODE === "session";

export async function runWorker(request: WorkerRequest): Promise<unknown> {
  if (usePersistentWorker) {
    return sharedPersistentSession().execute(request);
  }

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

  return collectWorkerResponse(child, request);
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
