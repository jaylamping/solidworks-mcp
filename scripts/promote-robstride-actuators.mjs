#!/usr/bin/env node
/**
 * Promote Robstride RS00–RS05 SLDPRT from Desktop archive into Marengo vendor vault
 * and print measured envelopes (SolidWorks must be running).
 *
 *   node scripts/promote-robstride-actuators.mjs
 */
import { createHash } from "node:crypto";
import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

import { packageRoot, runWorker } from "./lib/worker-client.mjs";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const marengoRoot = process.env.MARENGO_ROOT
  ? path.resolve(process.env.MARENGO_ROOT)
  : path.resolve(packageRoot, "..", "marengo");

const vendorDir = path.join(marengoRoot, "hardware/cad/vendor");
const incomingDir = path.join(vendorDir, "incoming");

const MODELS = [
  {
    id: "rs00",
    partNumber: "RS00",
    desktopNative:
      "C:/Users/joeyl/OneDrive/Desktop/robot/Actuators/RobStride/RS00/RS00.SLDPRT",
    incomingName: "robstride_rs00_vendor.SLDPRT",
    nativeName: "vendor_robstride_rs00_vendor.SLDPRT",
  },
  {
    id: "rs02",
    partNumber: "RS02",
    desktopNative:
      "C:/Users/joeyl/OneDrive/Desktop/robot/Actuators/RobStride/RS02/RS02.SLDPRT",
    incomingName: "robstride_rs02_vendor.SLDPRT",
    nativeName: "vendor_robstride_rs02_vendor.SLDPRT",
  },
  {
    id: "rs03",
    partNumber: "RS03",
    desktopNative:
      "C:/Users/joeyl/OneDrive/Desktop/robot/Actuators/RobStride/RS03/RS03.SLDPRT",
    incomingName: "robstride_rs03_vendor.SLDPRT",
    nativeName: "vendor_robstride_rs03_vendor.SLDPRT",
  },
  {
    id: "rs04",
    partNumber: "RS04",
    desktopNative:
      "C:/Users/joeyl/OneDrive/Desktop/robot/Actuators/RobStride/RS04/RS04.SLDPRT",
    incomingName: "robstride_rs04_vendor.SLDPRT",
    nativeName: "vendor_robstride_rs04_vendor.SLDPRT",
  },
  {
    id: "rs05",
    partNumber: "RS05",
    desktopNative:
      "C:/Users/joeyl/OneDrive/Desktop/robot/Actuators/RobStride/RS05/RS05.SLDPRT",
    incomingName: "robstride_rs05_vendor.SLDPRT",
    nativeName: "vendor_robstride_rs05_vendor.SLDPRT",
  },
];

async function sha256(filePath) {
  const data = await fs.readFile(filePath);
  return createHash("sha256").update(data).digest("hex");
}

async function copyNative(model) {
  await fs.mkdir(incomingDir, { recursive: true });
  const incomingPath = path.join(incomingDir, model.incomingName);
  const nativePath = path.join(vendorDir, model.nativeName);
  await fs.copyFile(model.desktopNative, incomingPath);
  await fs.copyFile(model.desktopNative, nativePath);
  const digest = await sha256(nativePath);
  return { incomingPath, nativePath, sha256: digest };
}

async function measure(model, nativePath) {
  return runWorker("actuator_get_envelope", { model: model.id, vendor_path: nativePath });
}

async function main() {
  const report = [];
  for (const model of MODELS) {
    const copied = await copyNative(model);
    const measured = await measure(model, copied.nativePath);
    const sizeMm = measured?.measured?.sizeMm ?? null;
    report.push({
      id: model.id,
      partNumber: model.partNumber,
      nativePath: copied.nativePath,
      incomingPath: copied.incomingPath,
      sha256: copied.sha256,
      sizeMm,
      envelope: measured,
    });
  }
  console.log(JSON.stringify({ marengoRoot, report }, null, 2));
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : String(error));
  process.exit(1);
});
