/**
 * Authoritative one-line summaries for worker-backed tools.
 *
 * The descriptions below were harvested once from the legacy read/write tool
 * registrations. That legacy code is intentionally not read at build time.
 */
export const DESCRIPTION_BY_COMMAND = {
  open: "Open a SolidWorks or STEP document from an allowed CAD root.",
  export: "Export a CAD document to STEP, STL, PDF, or PNG under an allowed CAD root.",
  save_document: "Save the active or specified SolidWorks document.",
  set_custom_properties: "Set custom properties on a part or assembly and optionally save it.",
  measure: "Return bounding-box and mass-property metadata for a CAD document.",
  list_features: "List top-level feature tree entries for a CAD document.",
  inspect_document: "Return document type, path, saved state, units, and custom properties.",
  list_components: "Return the assembly component tree with paths, quantities, and states.",
  list_reference_geometry: "List named reference planes, axes, coordinate systems, and points.",
  list_bom: "Return a flat component list as a bill-of-materials precursor.",
  list_mates: "Return mate feature names and types for an assembly.",
  list_configurations: "List configuration names for a part document.",
  list_dimensions: "List driving dimensions in a part, optionally for one configuration.",
  list_interferences: "Run interference detection on an assembly.",
  get_component_transform: "Return the 4x4 transform matrix for a named assembly component.",
  get_persist_reference: "Capture a base64 persist reference for a component reference.",
  probe_feature_faces: "List face count, area, and planarity for a component feature.",
  get_feature_box: "Return the assembly-space bounding box for a named component feature.",
  component_mass_properties: "Return mass, center of mass, and inertia for a component.",
  assembly_diagnostics: "Summarize fixed, lightweight, suppressed, broken-reference, and mate state.",
  mate_coincident: "Add a coincident mate between two component references or selected faces.",
  mate_parallel: "Add a parallel mate between two component references or selected faces.",
  mate_distance: "Add a distance mate between two component references or selected faces.",
  mate_perpendicular: "Add a perpendicular mate between two component references or selected faces.",
  set_dimension: "Set a driving part dimension value in meters.",
  insert_component: "Insert a part into an assembly from an allowed path.",
  set_component_transform: "Apply a 4x4 transform matrix to an assembly component.",
  rebuild_document: "Rebuild the active model.",
  select_by_persist_reference: "Select an entity using a base64 persist reference.",
  resolve_lightweight: "Resolve all lightweight components in an assembly.",
};

const VERB_OVERRIDES = {
  add: "Add",
  activate: "Activate",
  close: "Close",
  clone: "Clone",
  copy: "Copy",
  create: "Create",
  delete: "Delete",
  diagnose: "Diagnose",
  dissolve: "Dissolve",
  ensure: "Ensure",
  explain: "Explain",
  export: "Export",
  feature: "Manage",
  get: "Get",
  import: "Import",
  insert: "Insert",
  list: "List",
  make: "Make",
  mate: "Create",
  measure: "Measure",
  mirror: "Mirror",
  new: "Create",
  pack: "Pack",
  probe: "Probe",
  rebuild: "Rebuild",
  replace: "Replace",
  reset: "Reset",
  resolve: "Resolve",
  save: "Save",
  select: "Select",
  set: "Set",
  sketch: "Create",
  transform: "Transform",
  unfix: "Unfix",
};

function humanize(token) {
  return token.replaceAll("_", " ").replace(/\b\w/g, (letter) => letter.toUpperCase());
}

export function deriveSummary(command, propertyNames = []) {
  const words = command.split("_");
  const verb = VERB_OVERRIDES[words[0]] ?? humanize(words[0]);
  const subject = words.slice(1).join(" ") || "SolidWorks state";
  const args = propertyNames.filter(Boolean).slice(0, 3).join(", ");
  const suffix = args ? ` using ${args}` : "";
  return `${verb} ${subject}${suffix}.`;
}

export function assertDescriptionLength(description, max = 200) {
  if (typeof description !== "string" || description.trim().length === 0) {
    throw new Error("Tool descriptions must be non-empty strings");
  }
  if (description.length > max) {
    throw new Error(`Tool description exceeds ${max} characters (${description.length}): ${description}`);
  }
  return description;
}

export function descriptionFor(command, schemaKey, propertyNames = []) {
  const authored = DESCRIPTION_BY_COMMAND[command];
  const description = authored ?? deriveSummary(command, propertyNames);
  assertDescriptionLength(description);
  return {
    description,
    source: authored ? "authored" : "derived",
  };
}
