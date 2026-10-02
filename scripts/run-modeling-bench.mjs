#!/usr/bin/env node
/**
 * Builds every part in scripts/modeling-bench/ through the MCP server against a
 * running SolidWorks and checks the resulting solid volume.
 *
 *   npm run bench:modeling            # all parts
 *   npm run bench:modeling -- flange  # parts whose name matches
 *
 * Output parts and PNG renders go to .demo/bench/ (override with MODELING_BENCH_OUT).
 */
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const benchDir = path.join(root, "scripts/modeling-bench");
const outDir = path.resolve(process.env.MODELING_BENCH_OUT ?? path.join(root, ".demo/bench"));
const filter = process.argv[2] ? new RegExp(process.argv[2], "i") : null;
const tolerance = 0.005;

fs.mkdirSync(outDir, { recursive: true });

const transport = new StdioClientTransport({
  command: "node",
  args: [path.join(root, "node_modules/tsx/dist/cli.mjs"), path.join(root, "src/index.ts")],
  cwd: root,
  env: {
    ...process.env,
    SOLIDWORKS_MCP_TOOL_TIER: "all",
    SOLIDWORKS_MCP_ALLOWED_ROOTS: outDir,
    SOLIDWORKS_MCP_PERSISTENT_WORKER: "1",
    SOLIDWORKS_MCP_AUTO_CHECKPOINT: "0",
  },
  stderr: "pipe",
});
const client = new Client({ name: "modeling-bench", version: "0.0.1" });
await client.connect(transport);

async function call(name, args) {
  const result = await client.callTool({ name, arguments: args }, undefined, { timeout: 300000 });
  const text = (result.content ?? []).map((c) => c.text ?? "").join("\n");
  let data;
  try {
    data = JSON.parse(text);
  } catch {
    data = { raw: text };
  }

  if (result.isError) {
    const error = new Error(`${name}: ${data.code ?? ""} ${data.message ?? text}`.trim());
    error.data = data;
    throw error;
  }

  return data;
}

/** Per-step assertions: volume (±0.5%), bodies, regions, and arbitrary dotted paths via `fields`. */
function checkStep(expect, data) {
  if (!expect) return [];
  const problems = [];
  const part = data?.part ?? data?.outcome?.part ?? data?.result?.part;
  if (expect.volume !== undefined) {
    const v = part?.volume;
    const w = expect.volume;
    const ok = typeof w === "number"
      ? v !== undefined && Math.abs(v - w) <= Math.max(0.01, Math.abs(w) * tolerance)
      : v !== undefined
        && (w.approx === undefined || Math.abs(v - w.approx) <= Math.abs(w.approx) * (w.tol ?? 0.05))
        && (w.min === undefined || v >= w.min)
        && (w.max === undefined || v <= w.max);
    if (!ok) problems.push(`volume ${v} != ${JSON.stringify(w)}`);
  }

  if (expect.bodies !== undefined && part?.solidBodies !== expect.bodies) {
    problems.push(`bodies ${part?.solidBodies} != ${expect.bodies}`);
  }

  if (expect.regions !== undefined && data?.regionCount !== expect.regions) {
    problems.push(`regions ${data?.regionCount} != ${expect.regions}`);
  }

  for (const [field, wanted] of Object.entries(expect.fields ?? {})) {
    const actual = field.split(".").reduce((o, k) => (o == null ? undefined : o[k]), data);
    let same;
    if (wanted && typeof wanted === "object" && !Array.isArray(wanted)) {
      // {approx, tol} = relative tolerance; {min, max} = range.
      same = typeof actual === "number"
        && (wanted.approx === undefined || Math.abs(actual - wanted.approx) <= Math.abs(wanted.approx) * (wanted.tol ?? 0.05))
        && (wanted.min === undefined || actual >= wanted.min)
        && (wanted.max === undefined || actual <= wanted.max);
    } else {
      same = typeof wanted === "number" && typeof actual === "number"
        ? Math.abs(actual - wanted) <= Math.max(0.01, Math.abs(wanted) * tolerance)
        : JSON.stringify(actual) === JSON.stringify(wanted);
    }

    if (!same) problems.push(`${field}=${JSON.stringify(actual)} != ${JSON.stringify(wanted)}`);
  }

  return problems;
}

const specs = fs
  .readdirSync(benchDir)
  .filter((f) => f.endsWith(".json"))
  .map((f) => JSON.parse(fs.readFileSync(path.join(benchDir, f), "utf8")))
  .filter((spec) => !filter || filter.test(spec.name));

const results = [];
for (const spec of specs) {
  const part = path.join(outDir, `${spec.name}.SLDPRT`);
  const started = Date.now();
  const record = { name: spec.name, ok: false };
  try {
    await call("solidworks_close_document", { path: part, save: false }).catch(() => {});
    await call("solidworks_new_document", { doc_type: "part", output_path: part, confirm: true });
    for (const [index, step] of spec.steps.entries()) {
      const args = JSON.parse(
        JSON.stringify(step.args).replaceAll("$PART", part.replaceAll("\\", "/")).replaceAll("$OUT", outDir.replaceAll("\\", "/")),
      );
      let data;
      try {
        data = await call(step.tool, args);
      } catch (error) {
        if (step.expect_error && error.data?.code === step.expect_error) {
          continue;
        }

        error.message = `step ${index} ${error.message}`;
        throw error;
      }

      if (step.expect_error) {
        throw new Error(`step ${index} ${step.tool}: expected error ${step.expect_error} but it succeeded`);
      }

      const problems = checkStep(step.expect, data);
      if (problems.length > 0) {
        const error = new Error(`step ${index} ${step.tool}: ${problems.join("; ")}`);
        error.data = { context: { result: data } };
        throw error;
      }
    }

    const report = await call("solidworks_part_report", { path: part });
    await call("solidworks_render_view", { path: part, output_path: path.join(outDir, `${spec.name}.png`) });
    await call("solidworks_save_document", { path: part });
    const volume = report.part?.volume ?? 0;
    const errors = report.features.filter((f) => f.error);
    const expected = spec.expect?.volume_mm3;
    const volumeOk = expected === undefined || Math.abs(volume - expected) <= expected * tolerance;
    const bodiesOk = spec.expect?.bodies === undefined || report.part?.solidBodies === spec.expect.bodies;
    Object.assign(record, {
      ok: volumeOk && bodiesOk && errors.length === 0,
      volume,
      expected,
      bodies: report.part?.solidBodies,
      featureErrors: errors.map((f) => `${f.name}:${f.error}`),
    });
  } catch (error) {
    record.error = error.message;
    record.context = error.data?.context;
    // Keep the failed state on disk for debugging.
    await call("solidworks_save_document", { path: part }).catch(() => {});
  }

  // Close each part so dozens of bench runs do not pile up documents (and memory) in SolidWorks.
  await call("solidworks_close_document", { path: part, save: false }).catch(() => {});
  record.seconds = Math.round((Date.now() - started) / 100) / 10;
  results.push(record);
  console.log(`${record.ok ? "PASS" : "FAIL"} ${spec.name} (${record.seconds}s)${record.error ? ` ${record.error}` : ` volume=${record.volume} expected=${record.expected}`}`);
}

await client.close();
const failed = results.filter((r) => !r.ok);
console.log(JSON.stringify({ ok: failed.length === 0, passed: results.length - failed.length, failed: failed.length, outDir }, null, 2));
process.exit(failed.length === 0 ? 0 : 1);
