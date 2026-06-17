#!/usr/bin/env node
import { searchApiDocs } from "../dist/api-docs/search.js";
import { cadConventionsCheck } from "../dist/marengo/cad-audit.js";
import { hardwareCoverage } from "../dist/marengo/coverage.js";
import {
  kinematicsConsistency,
  urdfExportPostcheck,
  urdfReadiness,
} from "../dist/marengo/urdf-audit.js";
import { SolidWorksWorkerError } from "../dist/errors.js";
import { registrySummary } from "../dist/vendor-registry.js";
import { runWorker } from "../dist/worker.js";

const ASM = "C:/code/marengo/hardware/cad/assemblies/marengo_torso_asm.SLDASM";
const PART = "C:/code/marengo/hardware/cad/parts/marengo_torso_layout.SLDPRT";

const results = [];

async function run(name, fn) {
  try {
    const data = await fn();
    results.push({ name, ok: true, data });
  } catch (error) {
    const structured =
      error instanceof SolidWorksWorkerError
        ? error.workerError
        : { message: error instanceof Error ? error.message : String(error) };
    results.push({ name, ok: false, error: structured });
  }
}

await run("solidworks_status", () => runWorker({ command: "status", args: { start_if_missing: false } }));
await run("solidworks_search_api_docs", async () => searchApiDocs("IModelDoc2", 3));
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
await run("solidworks_list_configurations", () =>
  runWorker({ command: "list_configurations", args: { path: PART } }),
);
await run("solidworks_list_dimensions", () =>
  runWorker({ command: "list_dimensions", args: { path: PART } }),
);
await run("solidworks_assembly_diagnostics", () =>
  runWorker({ command: "assembly_diagnostics", args: { path: ASM } }),
);
await run("solidworks_invoke_read_blocked", async () => {
  try {
    await runWorker({
      command: "invoke",
      args: { target: "app", member: "DeleteFeature", args: [] },
    });
    return { blocked: false };
  } catch (error) {
    return { blocked: true, error: error instanceof Error ? error.message : String(error) };
  }
});
await run("solidworks_diagnose_com", () => runWorker({ command: "diagnose_com", args: {} }));
await run("solidworks_get_mass_properties", () =>
  runWorker({ command: "get_mass_properties", args: { path: PART } }),
);
await run("marengo_cad_conventions_check", () => cadConventionsCheck({}));
await run("marengo_vendor_registry_summary", () =>
  registrySummary("C:/code/marengo/hardware/manifests/vendor-assets.json"),
);
await run("marengo_urdf_readiness", () => urdfReadiness({ path: ASM }));
await run("marengo_kinematics_consistency", () => kinematicsConsistency({ path: ASM }));
await run("marengo_urdf_export_postcheck", () => urdfExportPostcheck());
await run("marengo_hardware_coverage", () => hardwareCoverage({ path: ASM }));

// Error-mode probes — expect structured failures (ok: false with code)
await run("error_bad_path", async () => {
  await runWorker({ command: "open", args: { path: "C:/outside/not_allowed.SLDPRT" } });
});
await run("error_unknown_command", async () => {
  await runWorker({ command: "not_a_real_command", args: {} });
});
await run("error_confirm_required", async () => {
  await runWorker({ command: "delete_all_mates", args: { path: ASM } });
});
await run("error_missing_component", async () => {
  await runWorker({
    command: "mate_coincident",
    args: {
      path: ASM,
      component_1: "nonexistent_component_a",
      ref_1: "Origin",
      component_2: "nonexistent_component_b",
      ref_2: "Origin",
    },
  });
});
await run("error_explain_error", () =>
  runWorker({ command: "explain_error", args: { sw_error_code: 4, message: "Already constrained" } }),
);

const summary = results.map((row) => ({
  tool: row.name,
  ok: row.ok,
  error: row.ok ? null : row.error,
  keys: row.ok && row.data && typeof row.data === "object" ? Object.keys(row.data).slice(0, 8) : null,
}));

console.log(JSON.stringify({ summary }, null, 2));

const failed = results.filter((r) => !r.ok);
const errorProbes = ["error_bad_path", "error_unknown_command", "error_confirm_required", "error_missing_component"];
const unexpectedFailures = failed.filter((r) => !errorProbes.includes(r.name));
const errorProbeFailures = errorProbes.filter((name) => results.find((r) => r.name === name)?.ok);

if (errorProbeFailures.length) {
  console.error("Expected error probes succeeded unexpectedly:", errorProbeFailures);
  process.exit(1);
}

process.exit(unexpectedFailures.length > 0 ? 1 : 0);
