import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const workerProject = path.join(root, "workers/SolidWorksComWorker");

const LAYOUT_PART =
  "C:/code/marengo/hardware/cad/parts/marengo_torso_layout_revA.SLDPRT";

function runWorker(command, args) {
  const payload = JSON.stringify({ command, args });
  const result = spawnSync(
    "dotnet",
    ["run", "--project", workerProject, "--no-launch-profile"],
    {
      cwd: root,
      input: payload,
      encoding: "utf8",
      windowsHide: true,
    },
  );

  if (result.status !== 0) {
    throw new Error(`Worker exited ${result.status}: ${result.stderr || result.stdout}`);
  }

  const parsed = JSON.parse(result.stdout.trim());
  if (!parsed.ok) {
    throw new Error(parsed.error ?? "Worker failed");
  }

  return parsed.data;
}

try {
  const data = runWorker("layout_add_shoulder_mounts", {
    path: LAYOUT_PART,
    save: true,
    replace_existing: true,
    inner_rail_mm: 55,
    outer_poke_mm: 95,
    depth_offset_mm: 0,
  });
  console.log(JSON.stringify({ ok: true, data }, null, 2));
} catch (error) {
  console.log(
    JSON.stringify(
      { ok: false, error: error instanceof Error ? error.message : String(error) },
      null,
      2,
    ),
  );
  process.exit(1);
}
