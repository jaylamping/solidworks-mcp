/**
 * Deterministic smoke demo: 40 mm cube with Ø18 mm through-cylinders on
 * Front / Top / Right. Requires SolidWorks on this machine.
 *
 * Usage: npm run demo:build-part
 */
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { runWorker } from "./lib/worker-client.mjs";

const packageRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const demoDir = path.join(packageRoot, ".demo");
const partPath = path.join(demoDir, "DemoCube.SLDPRT");
const previewPath = path.join(demoDir, "DemoCube.png");

const SIZE_MM = 40;
const HOLE_DIAMETER_MM = 18;

/** Expected solid volume (m³) for the default demo dimensions (inclusion-exclusion). */
function expectedVolumeM3(sizeMm, holeDiameterMm) {
  const a = sizeMm / 1000;
  const r = holeDiameterMm / 2000;
  const cylinder = Math.PI * r * r * a;
  const pair = (16 / 3) * r * r * r;
  const triple = 8 * (2 - Math.SQRT2) * r * r * r;
  return a * a * a - 3 * cylinder + 3 * pair - triple;
}

function ensureDemoRootAllowed() {
  const existing = (process.env.SOLIDWORKS_MCP_ALLOWED_ROOTS || "")
    .split(";")
    .map((entry) => entry.trim())
    .filter(Boolean);
  const roots = new Set([...existing, demoDir, packageRoot]);
  process.env.SOLIDWORKS_MCP_ALLOWED_ROOTS = [...roots].join(";");
}

fs.mkdirSync(demoDir, { recursive: true });
ensureDemoRootAllowed();

const built = runWorker("demo_build_part", {
  size_mm: SIZE_MM,
  hole_diameter_mm: HOLE_DIAMETER_MM,
  output_path: partPath,
  confirm: true,
});

const mass = runWorker("get_mass_properties", { path: partPath });
const expected = expectedVolumeM3(SIZE_MM, HOLE_DIAMETER_MM);
const actual = Number(mass.volume);
const volumeOk = Number.isFinite(actual) && Math.abs(actual - expected) / expected < 1e-3;

const exported = runWorker("export", {
  path: partPath,
  output_path: previewPath,
  format: "png",
});

const summary = {
  ok: volumeOk && exported?.ok !== false && Boolean(built?.saved),
  shape: built.shape,
  dimensions: { size_mm: SIZE_MM, hole_diameter_mm: HOLE_DIAMETER_MM },
  partPath,
  previewPath,
  bossFeatureName: built.bossFeatureName,
  cutFeatureNames: built.cutFeatureNames,
  volume: {
    actual_m3: actual,
    expected_m3: expected,
    match: volumeOk,
  },
  tip: 'In Cursor chat (MCP running): ask "Build the demo part" — tool solidworks_demo_build_part with confirm: true.',
};

console.log(JSON.stringify(summary, null, 2));
if (!summary.ok) {
  process.exit(1);
}
