/**
 * DESTRUCTIVE: same class as layout-place (worker torso_frame_build_mates). Requires --confirm.
 */
import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

if (!process.argv.slice(2).includes("--confirm")) {
  console.error(
    "Refusing to run: torso-frame-autobuild.mjs modifies the frame assembly in SolidWorks.\n" +
      "  node scripts/torso-frame-autobuild.mjs --confirm",
  );
  process.exit(1);
}

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const workerProject = path.join(root, "workers/SolidWorksComWorker");
const FRAME_ASM = "C:/code/marengo/hardware/cad/assemblies/marengo_torso_frame_asm_revA.SLDASM";

function runWorker(command, args) {
  const payload = JSON.stringify({ command, args });
  const result = spawnSync("dotnet", ["run", "--project", workerProject, "--no-launch-profile"], {
    cwd: root,
    input: payload,
    encoding: "utf8",
    windowsHide: true,
  });
  if (result.status !== 0) {
    throw new Error(`Worker exited ${result.status}: ${result.stderr || result.stdout}`);
  }
  const parsed = JSON.parse(result.stdout.trim());
  if (!parsed.ok) {
    throw new Error(parsed.error ?? "Worker failed");
  }
  return parsed.data;
}

const data = runWorker("torso_frame_build_mates", {
  path: FRAME_ASM,
  include_brackets: true,
  rebuild_configs: true,
});

console.log(JSON.stringify(data, null, 2));
