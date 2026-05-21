/** @deprecated Use torso-frame-layout-place.mjs */
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import path from "node:path";

const script = path.join(path.dirname(fileURLToPath(import.meta.url)), "torso-frame-layout-place.mjs");
const r = spawnSync(process.execPath, [script], { stdio: "inherit", encoding: "utf8" });
process.exit(r.status ?? 1);
