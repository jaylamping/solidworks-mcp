import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const workerProject = path.join(root, "workers/SolidWorksComWorker");

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
  if (result.status !== 0) throw new Error(result.stderr || result.stdout);
  const parsed = JSON.parse(result.stdout.trim());
  if (!parsed.ok) throw new Error(parsed.error ?? "Worker failed");
  return parsed.data;
}

const asm = "C:/code/marengo/hardware/cad/assemblies/marengo_torso_asm_revA.SLDASM";

const components = runWorker("list_components", { path: asm });
const actuators = components.components.filter((c) =>
  String(c.name).includes("shoulder_roll"),
);

const boxes = {};
for (const a of actuators) {
  const prefix = a.name.includes("left") ? "left" : "right";
  boxes[prefix] = runWorker("get_component_box", {
    path: asm,
    component_name: a.name,
  });
}

const layoutBox = runWorker("get_feature_box", {
  path: asm,
  component_name: "marengo_torso_layout_revA",
  feature_name: "torso_inner_clear",
}).catch?.(() => null);

let innerClear = null;
try {
  innerClear = runWorker("get_feature_box", {
    path: asm,
    component_name: "marengo_torso_frame_asm_revA/marengo_torso_layout_revA",
    feature_name: "torso_inner_clear",
  });
} catch {
  try {
    innerClear = runWorker("get_feature_box", {
      path: asm,
      component_name: "marengo_torso_layout_revA",
      feature_name: "torso_inner_clear",
    });
  } catch {
    innerClear = null;
  }
}

console.log(
  JSON.stringify(
    {
      actuators: actuators.map((a) => ({
        name: a.name,
        isFixed: a.isFixed,
        visible: a.visible,
      })),
      motorBoxes: boxes,
      innerClearBox: innerClear,
      mateCount: runWorker("list_mates", { path: asm }).mateCount,
    },
    null,
    2,
  ),
);
