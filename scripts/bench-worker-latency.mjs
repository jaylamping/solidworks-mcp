#!/usr/bin/env node
/**
 * Measure per-call worker latency for status probes.
 * Usage:
 *   node scripts/bench-worker-latency.mjs
 *   SOLIDWORKS_MCP_PERSISTENT_WORKER=1 node scripts/bench-worker-latency.mjs
 *   node scripts/bench-worker-latency.mjs --out .audit/baseline-latency.json
 */
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { performance } from "node:perf_hooks";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const outFlag = process.argv.indexOf("--out");
const outPath =
  outFlag >= 0 && process.argv[outFlag + 1]
    ? path.resolve(process.argv[outFlag + 1])
    : null;
const iterations = Number(process.env.SOLIDWORKS_MCP_BENCH_ITERS ?? 5);

const workerModPath = path.join(root, "dist/worker.js");
if (!fs.existsSync(workerModPath)) {
  console.error("dist/worker.js missing. Run npm run build first.");
  process.exit(2);
}

const { runWorker } = await import(pathToFileURL(workerModPath).href);
const { closeSharedPersistentSession } = await import(
  pathToFileURL(path.join(root, "dist/com-session.js")).href
);

const samples = [];
let lastError = null;

for (let i = 0; i < iterations; i++) {
  const t0 = performance.now();
  try {
    await runWorker({ command: "status", args: { start_if_missing: false } });
    samples.push(performance.now() - t0);
  } catch (error) {
    lastError = error instanceof Error ? error.message : String(error);
    samples.push(null);
  }
}

const ok = samples.filter((s) => typeof s === "number");
ok.sort((a, b) => a - b);
const sum = ok.reduce((a, b) => a + b, 0);
const p50 = ok.length ? ok[Math.floor(ok.length * 0.5)] : null;
const p95 = ok.length ? ok[Math.min(ok.length - 1, Math.ceil(ok.length * 0.95) - 1)] : null;

const report = {
  ts: new Date().toISOString(),
  persistent: process.env.SOLIDWORKS_MCP_PERSISTENT_WORKER === "1",
  iterations,
  samplesMs: samples,
  okCount: ok.length,
  meanMs: ok.length ? sum / ok.length : null,
  p50Ms: p50,
  p95Ms: p95,
  minMs: ok.length ? ok[0] : null,
  maxMs: ok.length ? ok[ok.length - 1] : null,
  error: lastError,
};

console.log(JSON.stringify(report, null, 2));

if (outPath) {
  fs.mkdirSync(path.dirname(outPath), { recursive: true });
  fs.writeFileSync(outPath, `${JSON.stringify(report, null, 2)}\n`, "utf8");
}

await closeSharedPersistentSession();
if (ok.length === 0) process.exit(1);
