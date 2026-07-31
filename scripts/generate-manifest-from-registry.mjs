#!/usr/bin/env node
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

import { SCHEMA_BY_COMMAND } from "./schema-by-command.mjs";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const registryPath = path.join(root, "workers/SolidWorksComWorker/WorkerCommandRegistry.cs");
const manifestPath = path.join(root, "tools/manifest.json");

const HAND_REGISTERED = new Set([
  "invoke",
  "batch_invoke",
  "status",
  "urdf_readiness",
  "add_urdf_frame",
]);

const DESTRUCTIVE = new Set([
  "delete_all_mates",
  "delete_mates_in_range",
  "delete_mate",
  "close_all_documents",
  "add_urdf_frame",
  "clone_solid_body_part",
  "mirror_part_file",
  "make_component_independent",
  "replace_components_by_path",
  "replace_component_path",
  "delete_feature",
  "dissolve_component",
  "mirror_component",
  "feature_extrude_boss",
  "sketch_rectangle",
  "create_sketch",
  "new_document",
  "resolve_lightweight",
  "feature_extrude_cut",
  "feature_fillet",
  "round_side_arms_from_circle",
  "feature_chamfer",
  "feature_mirror",
  "feature_linear_pattern",
  "feature_circular_pattern",
  "set_material",
  "create_subassembly",
  "explode_view",
  "copy_with_mates",
  "create_drawing_from_model",
]);

const READ_ONLY = new Set([
  "measure",
  "list_features",
  "inspect_document",
  "list_components",
  "list_reference_geometry",
  "list_bom",
  "list_mates",
  "list_configurations",
  "list_dimensions",
  "get_component_box",
  "get_component_transform",
  "get_part_feature_box",
  "get_planar_face_index",
  "list_interferences",
  "mate_probe",
  "debug_mate_entities",
  "diagnose_part_save",
  "diagnose_com",
  "diagnose_document",
  "diagnose_selection",
  "resolve_selection",
  "assembly_diagnostics",
  "component_mass_properties",
  "explain_error",
  "get_mass_properties",
  "get_material",
  "list_bodies",
  "get_equations",
  "list_sketches",
  "get_selection",
  "list_display_states",
  "get_open_documents",
  "measure_distance",
  "get_component_references",
  "list_broken_references",
  "mate_try_coincident",
  "mate_try_parallel",
  "mate_try_distance",
  "mate_try_perpendicular",
  "mate_try_width",
  "probe_angle_travel",
  "list_sheet_views",
  "get_assembly_degrees_of_freedom",
  "import_step",
]);

const CORE = new Set([
  "open",
  "export",
  "measure",
  "inspect_document",
  "list_components",
  "list_mates",
  "list_reference_geometry",
  "mate_limit_angle",
  "save_document",
  "set_custom_properties",
  "mate_coincident",
  "mate_parallel",
  "rebuild_document",
  "checkpoint_document",
  "diagnose_com",
]);

function parseRegistryCommands(text) {
  const matches = [...text.matchAll(/\["([a-z0-9_]+)"\]\s*=/g)];
  return [...new Set(matches.map((m) => m[1]))].sort();
}

function tierFor(command) {
  if (CORE.has(command)) return "core";
  if (command.startsWith("diagnose_") || command.startsWith("debug_") || command.startsWith("mate_try_")) {
    return "debug";
  }
  return "extended";
}

function mcpName(command) {
  return `solidworks_${command}`;
}

function schemaFor(command) {
  const schema = SCHEMA_BY_COMMAND[command] ?? "optionalPath";
  if (DESTRUCTIVE.has(command) && schema === "optionalPath") {
    throw new Error(`Destructive command '${command}' must have an explicit schema mapping in schema-by-command.mjs`);
  }
  return schema;
}

const registryText = fs.readFileSync(registryPath, "utf8");
const commands = parseRegistryCommands(registryText);

const schemaErrors = [];
for (const command of commands) {
  if (HAND_REGISTERED.has(command)) {
    continue;
  }
  if (DESTRUCTIVE.has(command) && !(SCHEMA_BY_COMMAND[command] && SCHEMA_BY_COMMAND[command] !== "optionalPath")) {
    schemaErrors.push(command);
  }
}
if (schemaErrors.length) {
  throw new Error(
    `Destructive commands missing explicit schema mapping: ${schemaErrors.join(", ")}`,
  );
}

const tools = commands
  .filter((command) => !HAND_REGISTERED.has(command))
  .map((command) => {
    const destructive = DESTRUCTIVE.has(command);
    const readOnly = READ_ONLY.has(command) || command.startsWith("list_") || command.startsWith("get_");
    return {
      name: mcpName(command),
      workerCommand: command,
      tier: tierFor(command),
      readOnly,
      destructive,
      confirmRequired: destructive,
      description: `Worker command: ${command}`,
      tags: [command.split("_")[0]],
      domains: command.includes("mate") ? ["assembly", "mate"] : ["document"],
      schema: schemaFor(command),
    };
  });

const manifest = { version: "1", tools };
fs.mkdirSync(path.dirname(manifestPath), { recursive: true });
fs.writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`);
console.log(`Wrote ${tools.length} tools to ${manifestPath}`);
