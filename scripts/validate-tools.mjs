#!/usr/bin/env node
import { searchApiDocs } from "../dist/api-docs/search.js";
import { SolidWorksWorkerError } from "../dist/errors.js";
import { runWorker } from "../dist/worker.js";

const results = [];

async function run(name, fn, { expectedCodes = [], allowUnavailable = false } = {}) {
  try {
    const data = await fn();
    results.push({ name, ok: true, data });
  } catch (error) {
    const structured =
      error instanceof SolidWorksWorkerError
        ? error.workerError
        : { message: error instanceof Error ? error.message : String(error) };
    const unavailable = !structured.code || ["SOLIDWORKS_NOT_RUNNING", "WORKER_START_FAILED"].includes(structured.code);
    const expected = expectedCodes.length === 0 || expectedCodes.includes(structured.code) || (allowUnavailable && unavailable);
    results.push({ name, ok: expected, expected, error: structured });
  }
}

// These probes are useful with or without SolidWorks. A missing worker or active
// document is reported in the output but does not make CI depend on a CAD session.
await run("solidworks_status", () =>
  runWorker({ command: "status", args: { start_if_missing: false } }),
  { allowUnavailable: true },
);
await run("solidworks_urdf_readiness_active_document", () =>
  runWorker({ command: "urdf_readiness", args: {} }),
  { expectedCodes: ["NO_ACTIVE_DOCUMENT"], allowUnavailable: true },
);
await run("solidworks_search_api_docs", () => searchApiDocs("IModelDoc2", 3));

// PathGuard must reject caller-supplied paths when no roots are configured.
await run(
  "error_path_open_fails_closed",
  () => runWorker({ command: "open", args: { path: "C:/outside/not_allowed.SLDPRT" } }),
  { expectedCodes: ["PATH_NOT_ALLOWED"], allowUnavailable: true },
);

const summary = results.map((row) => ({
  tool: row.name,
  ok: row.ok,
  expected: row.expected ?? null,
  error: row.ok && !row.error ? null : row.error,
  keys: row.data && typeof row.data === "object" ? Object.keys(row.data).slice(0, 8) : null,
}));

console.log(JSON.stringify({ summary }, null, 2));
process.exit(results.every((row) => row.ok) ? 0 : 1);
