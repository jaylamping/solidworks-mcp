/**
 * End-to-end smoke test for the two-body Common fallback and isolated bosses.
 *
 * Usage (PowerShell):
 *   node scripts/tests/combine-bodies-common-smoke.mjs
 *
 * The script drives the same one-shot worker protocol used by the other
 * scripts/tests files. It creates two overlapping, separately extruded bodies,
 * then requires combine_bodies to report synthesizedCommon=true. SolidWorks
 * must be installed and running (or startable through COM).
 */
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

import { runWorker } from "../lib/worker-client.mjs";

const packageRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const smokeDir = path.join(packageRoot, ".demo");
const partPath =
  process.env.COMBINE_BODIES_SMOKE_PART ??
  path.join(smokeDir, "CombineBodiesCommonSmoke.SLDPRT");

function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}

function ensureAllowedRoots() {
  const existing = (process.env.SOLIDWORKS_MCP_ALLOWED_ROOTS ?? "")
    .split(";")
    .map((entry) => entry.trim())
    .filter(Boolean);
  const roots = new Set([...existing, packageRoot, smokeDir, path.dirname(partPath)]);
  process.env.SOLIDWORKS_MCP_ALLOWED_ROOTS = [...roots].join(";");
}

function createOverlappingBody({ x1, y1, x2, y2, label }) {
  runWorker("create_sketch", { path: partPath, plane_name: "Front Plane" });
  runWorker("sketch_rectangle", {
    path: partPath,
    x1_m: x1,
    y1_m: y1,
    x2_m: x2,
    y2_m: y2,
  });
  runWorker("sketch_exit", { path: partPath });
  const sketches = runWorker("list_sketches", { path: partPath }).sketches ?? [];
  const sketchName = sketches.at(-1)?.name;
  assert(sketchName, `${label}: could not resolve the generated sketch`);
  const extrude = runWorker("feature_extrude_boss", {
    path: partPath,
    sketch_name: sketchName,
    depth_m: 0.02,
    merge: false,
    use_feat_scope: false,
    use_auto_select: false,
  });
  assert(extrude?.created === true, `${label}: merge:false extrude did not create a feature`);
  assert(extrude?.merge === false, `${label}: worker did not preserve merge:false`);
  return extrude;
}

function main() {
  fs.mkdirSync(smokeDir, { recursive: true });
  ensureAllowedRoots();

  runWorker("new_document", {
    doc_type: "part",
    output_path: partPath,
  });

  const outer = createOverlappingBody({
    x1: -0.01,
    y1: -0.01,
    x2: 0.01,
    y2: 0.01,
    label: "outer body",
  });
  const inner = createOverlappingBody({
    x1: -0.005,
    y1: -0.005,
    x2: 0.005,
    y2: 0.005,
    label: "inner body",
  });

  const bodies = runWorker("list_bodies", { path: partPath }).bodies ?? [];
  assert(bodies.length >= 2, `Expected at least two isolated bodies, got ${bodies.length}`);
  const bodyNames = bodies.map((body) => body.name).filter(Boolean);
  assert(bodyNames.length >= 2, "SolidWorks returned no usable body names");

  const common = runWorker("combine_bodies", {
    path: partPath,
    operation: "common",
    body_names: bodyNames.slice(0, 2),
  });
  assert(
    common?.synthesizedCommon === true,
    `Expected synthesizedCommon=true; native/common result was ${JSON.stringify(common)}`,
  );

  console.log(
    JSON.stringify(
      {
        ok: true,
        partPath,
        outerFeature: outer.featureName,
        innerFeature: inner.featureName,
        isolatedBodyCountBeforeCommon: bodies.length,
        synthesizedCommon: common.synthesizedCommon,
        attempts: common.attempts,
      },
      null,
      2,
    ),
  );
}

try {
  main();
} catch (error) {
  console.error(
    `combine-bodies-common-smoke failed. SolidWorks COM and a usable part document are required: ${
      error instanceof Error ? error.message : String(error)
    }`,
  );
  process.exit(1);
}
