#!/usr/bin/env node
import { runWorker } from "./lib/worker-client.mjs";

const MODELS = [
  { id: "rs00", path: "C:/code/marengo/cad/vendor/vendor_robstride_rs00_vendor.SLDPRT" },
  { id: "rs02", path: "C:/code/marengo/cad/vendor/vendor_robstride_rs02_vendor.SLDPRT" },
  { id: "rs03", path: "C:/code/marengo/cad/vendor/vendor_robstride_rs03_vendor.SLDPRT" },
  { id: "rs04", path: "C:/code/marengo/cad/vendor/vendor_robstride_rs04_vendor.SLDPRT" },
  { id: "rs05", path: "C:/code/marengo/cad/vendor/vendor_robstride_rs05_vendor.SLDPRT" },
];

const report = [];

for (const model of MODELS) {
  try {
    await runWorker("open", { path: model.path, start_if_missing: true });
    const envelope = await runWorker("actuator_get_envelope", {
      model: model.id,
      vendor_path: model.path,
    });
    const measure = await runWorker("measure", { path: model.path });
    report.push({
      model: model.id,
      path: model.path,
      sizeMm: envelope?.measured?.sizeMm ?? null,
      envelope,
      mass: measure?.mass ?? null,
      boundingBox: measure?.boundingBox ?? null,
    });
  } catch (error) {
    report.push({
      model: model.id,
      path: model.path,
      error: error instanceof Error ? error.message : String(error),
    });
  }
}

console.log(JSON.stringify({ report }, null, 2));
process.exit(report.some((row) => row.error) ? 1 : 0);
