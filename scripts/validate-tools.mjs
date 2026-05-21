import { cadConventionsCheck } from "../dist/marengo/cad-audit.js";
import { hardwareCoverage } from "../dist/marengo/coverage.js";
import {
  kinematicsConsistency,
  urdfExportPostcheck,
  urdfReadiness,
} from "../dist/marengo/urdf-audit.js";
import { registrySummary } from "../dist/vendor-registry.js";
import { runWorker } from "../dist/worker.js";

const ASM = "C:/code/marengo/hardware/cad/assemblies/marengo_torso_asm_revA.SLDASM";
const PART = "C:/code/marengo/hardware/cad/parts/marengo_torso_layout_revA.SLDPRT";

const results = [];

async function run(name, fn) {
  try {
    const data = await fn();
    results.push({ name, ok: true, data });
  } catch (error) {
    results.push({ name, ok: false, error: error instanceof Error ? error.message : String(error) });
  }
}

await run("solidworks_status", () => runWorker({ command: "status", args: { start_if_missing: false } }));
await run("solidworks_list_reference_geometry", () =>
  runWorker({ command: "list_reference_geometry", args: { path: ASM } }),
);
await run("solidworks_inspect_document", () =>
  runWorker({ command: "inspect_document", args: { path: ASM } }),
);
await run("solidworks_list_components", () => runWorker({ command: "list_components", args: { path: ASM } }));
await run("solidworks_list_bom", () => runWorker({ command: "list_bom", args: { path: ASM } }));
await run("solidworks_measure", () => runWorker({ command: "measure", args: { path: PART } }));
await run("solidworks_list_features", () => runWorker({ command: "list_features", args: { path: PART } }));
await run("marengo_cad_conventions_check", () => cadConventionsCheck({}));
await run("marengo_vendor_registry_summary", () => registrySummary("C:/code/marengo/hardware/manifests/vendor-assets.json"));
await run("marengo_urdf_readiness", () => urdfReadiness({ path: ASM }));
await run("marengo_kinematics_consistency", () => kinematicsConsistency({ path: ASM }));
await run("marengo_urdf_export_postcheck", () => urdfExportPostcheck());
await run("marengo_hardware_coverage", () => hardwareCoverage({ path: ASM }));

const summary = results.map((row) => ({
  tool: row.name,
  ok: row.ok,
  error: row.error ?? null,
  keys: row.ok && row.data && typeof row.data === "object" ? Object.keys(row.data).slice(0, 8) : null,
}));

console.log(JSON.stringify({ summary, urdfReadinessSample: results.find((r) => r.name === "marengo_urdf_readiness")?.data }, null, 2));

const failed = results.filter((r) => !r.ok);
process.exit(failed.length > 0 ? 1 : 0);
