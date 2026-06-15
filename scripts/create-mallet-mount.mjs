import path from "node:path";
import { fileURLToPath } from "node:url";
import { runWorker } from "./lib/worker-client.mjs";

const packageRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const defaultPartPath = path.resolve(
  packageRoot,
  "../marengo/hardware/cad/parts/marengo_mallet_mount_rs03.SLDPRT",
);

const handleDiameterMm = Number(process.argv[2] ?? "26");
const partPath = process.argv[3] ? path.resolve(process.argv[3]) : defaultPartPath;

const result = runWorker("create_mallet_mount", {
  part_path: partPath.replace(/\\/g, "/"),
  handle_diameter_mm: handleDiameterMm,
  save: true,
});

console.log(JSON.stringify(result, null, 2));
