import { spawn, type ChildProcess } from "node:child_process";
import { randomUUID } from "node:crypto";
import fs from "node:fs";
import readline from "node:readline";

import { packageRoot, workerDllPath, workerProjectPath } from "./config.js";
import { parseWorkerError, SolidWorksWorkerError, type WorkerError } from "./errors.js";

const PROTOCOL = 1 as const;

export interface WorkerCall {
  command: string;
  args?: Record<string, unknown>;
}

type ReadyFrame = { kind: "ready"; protocol: number; pid: number };
type ResponseFrame =
  | { kind: "response"; id: string; ok: true; data: unknown }
  | { kind: "response"; id: string; ok: false; error: WorkerError | string };

export interface ComSession {
  execute(call: WorkerCall): Promise<unknown>;
  close(): Promise<void>;
}

export interface ComSessionOptions {
  mode: "persistent" | "ephemeral";
  startupTimeoutMs?: number;
  requestTimeoutMs?: number;
}

class PersistentComSession implements ComSession {
  private child: ChildProcess | null = null;
  private reader: readline.Interface | null = null;
  private ready: Promise<void> | null = null;
  private queue: Promise<unknown> = Promise.resolve();
  private poisoned = false;
  private readonly startupTimeoutMs: number;
  private readonly requestTimeoutMs: number;

  constructor(options: ComSessionOptions) {
    this.startupTimeoutMs = options.startupTimeoutMs ?? 30_000;
    this.requestTimeoutMs = options.requestTimeoutMs ?? 300_000;
  }

  execute(call: WorkerCall): Promise<unknown> {
    const run = this.queue.then(() => this.executeOne(call));
    this.queue = run.then(
      () => undefined,
      () => undefined,
    );
    return run;
  }

  async close(): Promise<void> {
    if (!this.child || this.child.killed) {
      this.child = null;
      this.reader = null;
      this.ready = null;
      return;
    }

    try {
      await this.executeOne({ command: "__shutdown__", args: {} });
    } catch {
      try {
        this.child.kill();
      } catch {
        /* ignore */
      }
    } finally {
      this.child = null;
      this.reader = null;
      this.ready = null;
      this.poisoned = false;
    }
  }

  private async executeOne(call: WorkerCall): Promise<unknown> {
    if (this.poisoned) {
      await this.recreateChild();
    } else {
      await this.ensureChild();
    }

    const child = this.child;
    const reader = this.reader;
    if (!child?.stdin || !reader) {
      throw new Error("SolidWorks session worker is not available");
    }

    const id = randomUUID();
    const frame = {
      kind: "request" as const,
      protocol: PROTOCOL,
      id,
      command: call.command,
      args: call.args ?? {},
    };

    const responsePromise = this.readResponse(reader, id, this.requestTimeoutMs);
    const written = child.stdin.write(`${JSON.stringify(frame)}\n`);
    if (!written) {
      await new Promise<void>((resolve) => child.stdin?.once("drain", () => resolve()));
    }

    let response: ResponseFrame;
    try {
      response = await responsePromise;
    } catch (error) {
      this.poisonChild();
      const message = error instanceof Error ? error.message : String(error);
      throw new SolidWorksWorkerError({
        code: "WORKER_OUTCOME_UNKNOWN",
        message:
          `${message}. The request may have already executed in SolidWorks; it was not retried.`,
        category: "timeout",
        remediation: [
          "Inspect SolidWorks and checkpoints before repeating a mutating command.",
          "Retry only after confirming the previous attempt did not apply.",
        ],
      });
    }

    if (!response.ok) {
      const envelope =
        typeof response.error === "string"
          ? ({ code: "WORKER_ERROR", message: response.error, category: "worker" as const } satisfies WorkerError)
          : parseWorkerError(response.error);
      if (envelope) {
        throw new SolidWorksWorkerError(envelope);
      }
      throw new Error(typeof response.error === "string" ? response.error : "SolidWorks worker failed");
    }

    return response.data ?? {};
  }

  private async ensureChild(): Promise<void> {
    if (this.child && !this.child.killed && this.ready) {
      await this.ready;
      return;
    }
    await this.recreateChild();
  }

  private async recreateChild(): Promise<void> {
    this.disposeChildHandles();
    this.poisoned = false;

    const dll = workerDllPath();
    const useDll = fs.existsSync(dll);
    const child = spawn(
      "dotnet",
      useDll ? ["exec", dll, "--session"] : ["run", "--project", workerProjectPath(), "--no-launch-profile", "--", "--session"],
      {
        cwd: packageRoot(),
        stdio: ["pipe", "pipe", "pipe"],
        windowsHide: true,
        env: {
          ...process.env,
          SOLIDWORKS_MCP_WORKER_MODE: "session",
        },
      },
    );

    const reader = readline.createInterface({ input: child.stdout!, crlfDelay: Infinity });
    this.child = child;
    this.reader = reader;

    child.stderr?.on("data", (chunk: Buffer) => {
      process.stderr.write(`[sw-worker] ${chunk.toString("utf8")}`);
    });

    child.on("exit", () => {
      this.poisoned = true;
    });

    this.ready = this.waitForReady(reader, this.startupTimeoutMs);
    await this.ready;
  }

  private disposeChildHandles(): void {
    if (this.reader) {
      this.reader.close();
      this.reader = null;
    }
    if (this.child && !this.child.killed) {
      try {
        this.child.kill();
      } catch {
        /* ignore */
      }
    }
    this.child = null;
    this.ready = null;
  }

  private poisonChild(): void {
    this.poisoned = true;
    this.disposeChildHandles();
  }

  private waitForReady(reader: readline.Interface, timeoutMs: number): Promise<void> {
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        cleanup();
        reject(new Error(`Session worker ready handshake timed out after ${timeoutMs}ms`));
      }, timeoutMs);

      const onLine = (line: string) => {
        const trimmed = line.trim();
        if (!trimmed) return;
        let parsed: ReadyFrame;
        try {
          parsed = JSON.parse(trimmed) as ReadyFrame;
        } catch (error) {
          cleanup();
          reject(new Error(`Session worker ready frame was not JSON: ${trimmed}`));
          return;
        }
        if (parsed.kind !== "ready" || parsed.protocol !== PROTOCOL) {
          cleanup();
          reject(new Error(`Session worker protocol mismatch: ${trimmed}`));
          return;
        }
        cleanup();
        resolve();
      };

      const cleanup = () => {
        clearTimeout(timer);
        reader.off("line", onLine);
      };

      reader.on("line", onLine);
    });
  }

  private readResponse(
    reader: readline.Interface,
    id: string,
    timeoutMs: number,
  ): Promise<ResponseFrame> {
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        cleanup();
        reject(new Error(`Session worker response timed out after ${timeoutMs}ms`));
      }, timeoutMs);

      const onLine = (line: string) => {
        const trimmed = line.trim();
        if (!trimmed) return;
        let parsed: ResponseFrame;
        try {
          parsed = JSON.parse(trimmed) as ResponseFrame;
        } catch {
          cleanup();
          reject(new Error(`Session worker returned non-JSON: ${trimmed}`));
          return;
        }
        if (parsed.kind !== "response") {
          cleanup();
          reject(new Error(`Session worker returned unexpected frame: ${trimmed}`));
          return;
        }
        if (parsed.id !== id) {
          cleanup();
          reject(new Error(`Session worker response id mismatch (expected ${id}, got ${parsed.id})`));
          return;
        }
        cleanup();
        resolve(parsed);
      };

      const cleanup = () => {
        clearTimeout(timer);
        reader.off("line", onLine);
      };

      reader.on("line", onLine);
    });
  }
}

let sharedSession: PersistentComSession | null = null;

export function createComSession(options: ComSessionOptions): ComSession {
  return new PersistentComSession(options);
}

export function sharedPersistentSession(): ComSession {
  if (!sharedSession) {
    sharedSession = new PersistentComSession({ mode: "persistent" });
    const shutdown = () => {
      void sharedSession?.close();
    };
    process.once("SIGINT", shutdown);
    process.once("SIGTERM", shutdown);
  }
  return sharedSession;
}

export async function closeSharedPersistentSession(): Promise<void> {
  if (!sharedSession) return;
  await sharedSession.close();
  sharedSession = null;
}
