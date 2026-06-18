/**
 * Drop  from Marengo CAD filenames and SW component instance names.
 * Parts: *.SLDPRT → *.SLDPRT
 * Assemblies: *_asm.SLDASM → *_asm.SLDASM; *.SLDASM → *_asm.SLDASM
 */
import fs from "node:fs";
import path from "node:path";
import { runWorker } from "./lib/worker-client.mjs";

const MARENGO = "C:/code/marengo";
const PARTS_DIR = `${MARENGO}/cad/parts`;
const ASM_DIR = `${MARENGO}/cad/assemblies`;
const EXPORTS_DIR = `${MARENGO}/cad/exports`;

/** @type {{ oldBase: string, newBase: string, ext: ".SLDPRT" | ".SLDASM" }[]} */
const RENAMES = [
  { oldBase: "marengo_torso_layout", newBase: "marengo_torso_layout", ext: ".SLDPRT" },
  {
    oldBase: "marengo_shoulder_pitch_mount_bracket_left",
    newBase: "marengo_shoulder_pitch_mount_bracket_left",
    ext: ".SLDPRT",
  },
  {
    oldBase: "marengo_shoulder_pitch_mount_bracket_right",
    newBase: "marengo_shoulder_pitch_mount_bracket_right",
    ext: ".SLDPRT",
  },
  {
    oldBase: "marengo_torso_upper_compute_shelf",
    newBase: "marengo_torso_upper_compute_shelf",
    ext: ".SLDPRT",
  },
  { oldBase: "marengo_torso_frame_asm", newBase: "marengo_torso_frame_asm", ext: ".SLDASM" },
  {
    oldBase: "marengo_torso_upper_compute_stack_asm",
    newBase: "marengo_torso_upper_compute_stack_asm",
    ext: ".SLDASM",
  },
  { oldBase: "marengo_torso_asm", newBase: "marengo_torso_asm", ext: ".SLDASM" },
];

const PARENT_ASSEMBLIES = [
  `${ASM_DIR}/marengo.SLDASM`,
  `${ASM_DIR}/marengo_torso_asm.SLDASM`,
  `${ASM_DIR}/marengo_torso_asm.SLDASM`,
  `${ASM_DIR}/marengo_torso_frame_asm.SLDASM`,
  `${ASM_DIR}/marengo_torso_frame_asm.SLDASM`,
  `${ASM_DIR}/marengo_torso_upper_compute_stack_asm.SLDASM`,
  `${ASM_DIR}/marengo_torso_upper_compute_stack_asm.SLDASM`,
].filter((p, i, a) => a.indexOf(p) === i);

const steps = [];

function partPath(base) {
  return `${PARTS_DIR}/${base}.SLDPRT`;
}

function asmPath(base) {
  return `${ASM_DIR}/${base}.SLDASM`;
}

function fullPath(entry) {
  return entry.ext === ".SLDPRT" ? partPath(entry.oldBase) : asmPath(entry.oldBase);
}

function newFullPath(entry) {
  return entry.ext === ".SLDPRT" ? partPath(entry.newBase) : asmPath(entry.newBase);
}

function saveAs(oldPath, newPath) {
  if (fs.existsSync(newPath)) {
    return { skipped: true, newPath };
  }
  if (!fs.existsSync(oldPath)) {
    return { missing: true, oldPath };
  }
  return runWorker("export", {
    path: oldPath,
    output_path: newPath,
    start_if_missing: true,
  });
}

function replaceInParents(fromPath, toPath) {
  const results = [];
  for (const parent of PARENT_ASSEMBLIES) {
    if (!fs.existsSync(parent)) {
      continue;
    }
    try {
      results.push({
        parent,
        result: runWorker("replace_components_by_path", {
          path: parent,
          from_part_path: fromPath.replace(/\\/g, "/"),
          to_part_path: toPath.replace(/\\/g, "/"),
          configuration: "Default",
          save: true,
        }),
      });
    } catch (error) {
      results.push({
        parent,
        error: error instanceof Error ? error.message : String(error),
      });
    }
  }
  return results;
}

function renameInstance(fromBase, toBase) {
  const results = [];
  for (const parent of PARENT_ASSEMBLIES) {
    if (!fs.existsSync(parent)) {
      continue;
    }
    try {
      results.push({
        parent,
        result: runWorker("rename_component", { path: parent, from: fromBase, to: toBase }),
      });
    } catch (error) {
      const msg = error instanceof Error ? error.message : String(error);
      if (!msg.includes("Component not found")) {
        results.push({ parent, error: msg });
      }
    }
  }
  return results;
}

try {
  try {
    runWorker("close_all_documents", {});
  } catch {
    // SolidWorks may not be running — file-only renames still proceed.
  }

  for (const entry of RENAMES) {
    const oldPath = fullPath(entry);
    const newPath = newFullPath(entry);
    steps.push({
      step: `save_as_${entry.newBase}`,
      result: saveAs(oldPath, newPath),
    });
    steps.push({
      step: `replace_${entry.newBase}`,
      result: replaceInParents(oldPath, newPath),
    });
    steps.push({
      step: `rename_instance_${entry.newBase}`,
      result: renameInstance(entry.oldBase, entry.newBase),
    });
    if (
      fs.existsSync(oldPath) &&
      fs.existsSync(newPath) &&
      path.resolve(oldPath).toLowerCase() !== path.resolve(newPath).toLowerCase()
    ) {
      try {
        fs.unlinkSync(oldPath);
        steps.push({ step: `removed_${entry.oldBase}`, path: oldPath });
      } catch (error) {
        steps.push({
          step: `removed_${entry.oldBase}_failed`,
          path: oldPath,
          error: error instanceof Error ? error.message : String(error),
        });
      }
    }
  }

  for (const asm of [`${ASM_DIR}/marengo_torso_asm.SLDASM`, `${ASM_DIR}/marengo_torso_asm.SLDASM`]) {
    if (fs.existsSync(asm)) {
      steps.push({ step: "save_asm", result: runWorker("save_document", { path: asm }) });
    }
  }

  // Export STLs
  for (const entry of RENAMES.filter((e) => e.ext === ".SLDPRT")) {
    const oldStl = `${EXPORTS_DIR}/${entry.oldBase}.STL`;
    const newStl = `${EXPORTS_DIR}/${entry.newBase}.STL`;
    if (fs.existsSync(oldStl) && !fs.existsSync(newStl)) {
      fs.renameSync(oldStl, newStl);
      steps.push({ step: "renamed_stl", from: oldStl, to: newStl });
    }
  }

  const previewOld = `${ASM_DIR}/marengo_torso_frame_asm_preview.png`;
  const previewNew = `${ASM_DIR}/marengo_torso_frame_asm_preview.png`;
  if (fs.existsSync(previewOld) && !fs.existsSync(previewNew)) {
    fs.renameSync(previewOld, previewNew);
    steps.push({ step: "renamed_preview", from: previewOld, to: previewNew });
  }

  console.log(JSON.stringify({ ok: true, steps }, null, 2));
} catch (error) {
  console.log(
    JSON.stringify(
      {
        ok: false,
        steps,
        error: error instanceof Error ? error.message : String(error),
      },
      null,
      2,
    ),
  );
  process.exit(1);
}
