import { z } from "zod";

// Schemas for the selector-driven part-modeling tools. Every tool targets an
// explicit part via `path` so a failure can never spill into another open document.

export const partPathField = z
  .string()
  .min(1)
  .describe("Part document to edit (.SLDPRT). Required so edits never land in whichever document happens to be active.");

export const unitsField = z
  .enum(["mm", "cm", "m", "in"])
  .optional()
  .describe("Length units for every length and coordinate in this call. Default mm.");

const nameField = z.string().min(1).optional().describe("Rename the created feature.");

const rollbackField = z
  .boolean()
  .optional()
  .describe("Delete the feature again if it rebuilds with an error (default true), so the part stays clean.");

const point = z.array(z.number()).min(2).max(3);
const point3 = z.array(z.number()).length(3);

export const selectorSchema = z
  .union([z.string().min(1), z.record(z.string(), z.unknown())])
  .describe(
    [
      "Entity selector. A string names a plane/axis/sketch/point/feature (e.g. \"Top Plane\", \"Sketch2\").",
      "Objects (model coordinates, in `units`):",
      "{plane|axis|sketch|feature|point|coord_sys|body: \"<name>\"};",
      "{face: {near:[x,y,z], normal?:[nx,ny,nz], type?:\"plane|cylinder|cone|sphere|torus\", of_feature?, body?, max_distance?}} = the face nearest a point (default max_distance 2 mm);",
      "{edge: {near:[x,y,z], direction?:[dx,dy,dz], type?:\"line|circle\", radius?, on_face?:{face spec}, of_feature?}} = nearest edge;",
      "{vertex: {near:[x,y,z]}};",
      "{faces: {filters}} / {edges: {filters}} = every match. Filters: type, normal (faces), direction (line edges), axis (circle edges), radius, box:[xmin,ymin,zmin,xmax,ymax,zmax] (midpoint inside), of_feature, on_face (edges), body.",
    ].join(" "),
  );

const bodyRef = z
  .union([z.string().min(1), z.object({ near: point3 }), z.object({ body: z.string().min(1) })])
  .describe("Body name, or {near:[x,y,z]} for the body closest to a model point (names change as features are added).");
const bodyList = z.array(bodyRef).min(1);

const selectorList = z
  .union([selectorSchema, z.array(selectorSchema).min(1)])
  .describe("One selector or an array of selectors (see selector grammar on solidworks_sketch.on).");

// ---------------------------------------------------------------- sketch

const constructionField = { construction: z.boolean().optional().describe("Make it construction geometry.") };

const sketchEntitySchema = z.discriminatedUnion("type", [
  z.object({ type: z.literal("line"), from: point, to: point, ...constructionField }),
  z.object({ type: z.literal("centerline"), from: point, to: point, ...constructionField }).describe("Construction centerline; a revolve uses the sketch's centerline as its axis."),
  z.object({
    type: z.literal("circle"),
    center: point,
    radius: z.number().positive().optional(),
    diameter: z.number().positive().optional(),
    ...constructionField,
  }),
  z.object({
    type: z.literal("arc"),
    center: point,
    start: point,
    end: point,
    clockwise: z.boolean().optional().describe("Default counterclockwise from start to end."),
    ...constructionField,
  }),
  z.object({ type: z.literal("arc3"), start: point, mid: point, end: point, ...constructionField }).describe("Arc through three points."),
  z.object({ type: z.literal("rectangle"), corner1: point, corner2: point, ...constructionField }),
  z.object({
    type: z.literal("center_rectangle"),
    center: point,
    width: z.number().positive(),
    height: z.number().positive(),
    corner_radius: z.number().nonnegative().optional().describe("Round all four corners (tangent arcs)."),
    ...constructionField,
  }),
  z.object({
    type: z.literal("polyline"),
    points: z.array(point).min(2),
    closed: z.boolean().optional().describe("Connect last point back to the first."),
    corner_radius: z.number().nonnegative().optional().describe("Round every interior corner with tangent arcs."),
    ...constructionField,
  }),
  z.object({
    type: z.literal("slot"),
    start: point,
    end: point,
    width: z.number().positive().describe("Slot width (end-arc diameter). start/end are arc centers."),
    ...constructionField,
  }),
  z.object({
    type: z.literal("polygon"),
    center: point,
    sides: z.number().int().min(3),
    radius: z.number().positive(),
    radius_is: z.enum(["vertex", "inscribed"]).optional().describe("vertex (default) = center-to-corner; inscribed = center-to-flat (e.g. hex nut across-flats/2)."),
    rotation_deg: z.number().optional().describe("Angle of the first vertex, default 90."),
    ...constructionField,
  }),
  z.object({ type: z.literal("spline"), points: z.array(point).min(2), natural_ends: z.boolean().optional(), ...constructionField }),
  z.object({ type: z.literal("point"), at: point }),
  z.object({
    type: z.literal("text"),
    at: point.describe("Lower-left of the text."),
    text: z.string().min(1),
    height: z.number().positive().optional().describe("Character height (default 5)."),
    font: z.string().optional(),
    bold: z.boolean().optional(),
  }).describe("Sketch text: extrude it (emboss) or cut it (engrave)."),
  z.object({
    type: z.literal("convert_edges"),
    from: z.union([selectorSchema, z.array(selectorSchema)]).describe("Model edges, or a face (its boundary), projected into the sketch."),
    inner_loops: z.boolean().optional(),
  }),
  z.object({
    type: z.literal("offset_edges"),
    from: z.union([selectorSchema, z.array(selectorSchema)]).describe("Model edges or a face boundary to offset."),
    distance: z.union([z.number(), z.array(z.number()).min(1)]).describe("Offset distance(s): positive = outward from a face boundary, negative = inward. An array makes several offsets of the same edges (e.g. [-2.2, -3.4] for a lip ring)."),
    keep_original: z.boolean().optional().describe("Keep the projected originals as real geometry (default: construction)."),
  }),
  z.object({
    type: z.literal("ellipse"),
    center: point,
    major_radius: z.number().positive(),
    minor_radius: z.number().positive(),
    rotation_deg: z.number().optional(),
    ...constructionField,
  }),
]);

export const sketchSchema = z
  .object({
    path: partPathField,
    units: unitsField,
    on: selectorSchema
      .optional()
      .describe(
        "Where to sketch: a plane name (\"Front Plane\", \"Top Plane\", \"Right Plane\", or a reference plane) or a planar face selector {face:{near:[x,y,z]}}.",
      ),
    edit_sketch: z.string().min(1).optional().describe("Reopen this existing sketch and add the entities to it instead of creating a new one."),
    name: z.string().min(1).optional().describe("Name for the new sketch."),
    space: z
      .enum(["sketch", "model"])
      .optional()
      .describe("Coordinate space of entity points. sketch (default): 2D [x,y] in the sketch plane's own axes (see sketchFrameInModel in the result). model: 3D [x,y,z] model points, projected onto the sketch plane."),
    entities: z.array(sketchEntitySchema).optional(),
    remove: z
      .array(z.object({ near: point, max_distance: z.number().positive().optional() }))
      .optional()
      .describe("With edit_sketch: delete the line/arc/circle closest to each point (same coordinate space as entities; default max_distance 1) before adding entities. Combine with entities to move/resize geometry in undimensioned sketches."),
    atomic: z.boolean().optional().describe("If any entity fails, delete the new sketch and report (default true)."),
  })
  .refine((v) => v.on !== undefined || v.edit_sketch !== undefined, { message: "Provide on or edit_sketch." })
  .refine((v) => (v.entities?.length ?? 0) > 0 || (v.remove?.length ?? 0) > 0, { message: "Provide entities and/or remove." })
  .refine((v) => v.remove === undefined || v.edit_sketch !== undefined, { message: "remove requires edit_sketch." });

// ---------------------------------------------------------------- features

const endCondition = z
  .enum(["blind", "through_all", "up_to_next", "up_to_face", "offset_from_face", "mid_plane"])
  .describe("End condition. mid_plane extrudes depth split evenly on both sides.");

export const extrudeSchema = z.object({
  path: partPathField,
  units: unitsField,
  sketch: z.string().min(1).describe("Sketch (closed profile) to extrude."),
  mode: z.enum(["boss", "cut"]).optional().describe("boss adds material (default), cut removes it."),
  end: endCondition.optional().describe("Default blind."),
  depth: z.number().positive().optional().describe("Depth for blind/mid_plane, offset for offset_from_face."),
  up_to: selectorSchema.optional().describe("Face/plane for up_to_face / offset_from_face."),
  reverse: z.boolean().optional().describe("Flip the extrude direction (default: along the sketch normal; cuts go into the material)."),
  direction2: z
    .object({ end: endCondition.optional(), depth: z.number().positive().optional(), up_to: selectorSchema.optional() })
    .optional()
    .describe("Also extrude in the opposite direction."),
  draft_deg: z.number().min(0).max(89).optional(),
  draft_outward: z.boolean().optional(),
  thin: z
    .object({ thickness: z.number().positive(), side: z.enum(["one", "reverse", "mid", "two"]).optional(), cap_ends: z.boolean().optional() })
    .optional()
    .describe("Thin-feature extrude (wall of given thickness along open or closed contours)."),
  start_offset: z.number().optional().describe("Start the extrude offset from the sketch plane."),
  merge: z.boolean().optional().describe("Boss only: merge into existing bodies (default true); false makes a separate body."),
  scope_bodies: bodyList.optional().describe("Limit a cut / merge to these body names."),
  name: nameField,
  rollback_on_error: rollbackField,
});

export const revolveSchema = z.object({
  path: partPathField,
  units: unitsField,
  sketch: z.string().min(1).describe("Profile sketch. Must lie entirely on one side of the axis."),
  mode: z.enum(["boss", "cut"]).optional(),
  axis: selectorSchema.optional().describe("Axis: omit to use the sketch's single centerline; or an axis name, a linear edge, or an edge/line selector."),
  angle_deg: z.number().positive().max(360).optional().describe("Default 360."),
  mid_plane: z.boolean().optional(),
  reverse: z.boolean().optional(),
  angle2_deg: z.number().positive().max(360).optional().describe("Second-direction angle."),
  thin: z.object({ thickness: z.number().positive(), side: z.enum(["one", "reverse", "mid"]).optional() }).optional(),
  merge: z.boolean().optional(),
  name: nameField,
  rollback_on_error: rollbackField,
});

export const sweepSchema = z.object({
  path: partPathField,
  units: unitsField,
  profile: z.string().min(1).optional().describe("Closed profile sketch at the path start."),
  circular_profile_diameter: z.number().positive().optional().describe("Sweep a round section of this diameter instead of a profile sketch (tubes, wires, O-ring grooves)."),
  sweep_path: z.union([selectorSchema, z.array(selectorSchema)]).describe("Path: sketch or helix name, or edge selector(s). The profile should be pierced by / perpendicular to the path start."),
  guides: z.union([selectorSchema, z.array(selectorSchema)]).optional().describe("Guide curves (sketches/edges)."),
  path_alignment: z.enum(["auto", "none", "minimum_twist"]).optional().describe("auto: minimum twist for helix paths (threads, springs), none otherwise."),
  mode: z.enum(["boss", "cut"]).optional(),
  twist_deg: z.number().optional(),
  merge: z.boolean().optional(),
  name: nameField,
  rollback_on_error: rollbackField,
});

export const loftSchema = z.object({
  path: partPathField,
  units: unitsField,
  profiles: z.array(selectorSchema).min(2).describe("Ordered profiles: sketch names, planar faces, or points (first/last only)."),
  guides: z.array(selectorSchema).optional().describe("Guide-curve sketches/edges."),
  mode: z.enum(["boss", "cut"]).optional(),
  closed: z.boolean().optional().describe("Close the loft back to the first profile."),
  merge: z.boolean().optional(),
  name: nameField,
  rollback_on_error: rollbackField,
});

export const shellSchema = z.object({
  path: partPathField,
  units: unitsField,
  thickness: z.number().positive(),
  remove_faces: selectorList.optional().describe("Faces to open up (e.g. the top face). Omit for a closed hollow body."),
  outward: z.boolean().optional().describe("Add thickness outside the original body (default inward)."),
  name: nameField,
  rollback_on_error: rollbackField,
});

export const filletSchema = z.object({
  path: partPathField,
  units: unitsField,
  radius: z.number().positive(),
  edges: selectorList.describe("Edges to round. A face selector rounds all of that face's edges. Use {edges:{...filters}} to grab many at once."),
  name: nameField,
  rollback_on_error: rollbackField,
});

export const chamferSchema = z.object({
  path: partPathField,
  units: unitsField,
  distance: z.number().positive(),
  angle_deg: z.number().positive().max(89).optional().describe("Angle-distance chamfer angle (default 45)."),
  distance2: z.number().positive().optional().describe("Use distance-distance chamfer with this second distance."),
  edges: selectorList,
  name: nameField,
  rollback_on_error: rollbackField,
});

export const holeSchema = z.object({
  path: partPathField,
  units: unitsField,
  on: selectorSchema.describe("Planar face or plane the holes start from. Holes go into the material (normal opposite the face's outward normal)."),
  positions: z.array(point).min(1).describe("Hole centers. 2D sketch coords on the face, or 3D model points with space:\"model\"."),
  space: z.enum(["sketch", "model"]).optional().describe("Coordinate space for positions (default model for faces, since face sketch axes are not obvious)."),
  diameter: z.number().positive(),
  depth: z.number().positive().optional().describe("Omit for through-all."),
  counterbore: z.object({ diameter: z.number().positive(), depth: z.number().positive() }).optional(),
  countersink: z.object({ diameter: z.number().positive(), angle_deg: z.number().positive().max(179).optional() }).optional().describe("Countersink head diameter at the surface; angle default 90 (metric flat-head)."),
  name: nameField,
  rollback_on_error: rollbackField,
});

export const linearPatternSchema = z.object({
  path: partPathField,
  units: unitsField,
  features: z.array(z.string().min(1)).min(1).optional().describe("Seed features to pattern."),
  bodies: bodyList.optional().describe("Seed bodies to pattern (instead of features)."),
  direction: selectorSchema.describe("Direction reference: linear edge, axis, plane/planar face (uses its normal)."),
  count: z.number().int().min(2).describe("Total instances including the seed."),
  spacing: z.number().positive(),
  reverse: z.boolean().optional(),
  vector: z
    .array(z.number())
    .length(3)
    .optional()
    .describe("Intended pattern direction in model space, e.g. [0,0,1]. Edge directions have an arbitrary sign; with vector the tool checks where instances landed and flips if needed. Recommended."),
  direction2: z
    .object({ direction: selectorSchema, count: z.number().int().min(2), spacing: z.number().positive(), reverse: z.boolean().optional() })
    .optional(),
  name: nameField,
  rollback_on_error: rollbackField,
});

export const circularPatternSchema = z.object({
  path: partPathField,
  units: unitsField,
  features: z.array(z.string().min(1)).min(1).optional(),
  bodies: bodyList.optional(),
  axis: selectorSchema.describe("Axis name, cylindrical/conical face (its axis), circular edge (its axis), or linear edge."),
  count: z.number().int().min(2).describe("Total instances including the seed."),
  angle_deg: z.number().positive().max(360).optional().describe("Total angle covered with equal spacing (default 360)."),
  reverse: z.boolean().optional(),
  name: nameField,
  rollback_on_error: rollbackField,
});

export const mirrorSchema = z.object({
  path: partPathField,
  units: unitsField,
  plane: selectorSchema.describe("Mirror plane: plane name or planar face selector."),
  features: z.array(z.string().min(1)).min(1).optional(),
  bodies: bodyList.optional().describe("Mirror whole bodies instead of features."),
  merge: z.boolean().optional().describe("Bodies mode: merge the mirrored body with the original (default true)."),
  name: nameField,
  rollback_on_error: rollbackField,
});

export const refPlaneSchema = z.object({
  path: partPathField,
  units: unitsField,
  from: selectorSchema.optional().describe("Base plane or planar face (not needed with through)."),
  offset: z.number().optional().describe("Offset distance along the base normal (negative flips)."),
  angle_deg: z.number().optional().describe("Rotate about `about` by this angle instead of offsetting."),
  about: selectorSchema.optional().describe("Axis / linear edge for an angled plane."),
  through: z.array(selectorSchema).optional().describe("Alternative: plane through 3 vertices/points."),
  name: nameField,
});

export const refAxisSchema = z.object({
  path: partPathField,
  units: unitsField,
  from: z
    .array(selectorSchema)
    .min(1)
    .max(2)
    .describe("One cylindrical/conical face or linear edge; or two planes (intersection); or two vertices/points."),
  name: nameField,
});

export const partReportSchema = z.object({
  path: partPathField,
  units: unitsField,
  include_faces: z.boolean().optional().describe("List every face with surface type, center, normal/axis, area."),
  include_edges: z.boolean().optional().describe("List every edge with type, endpoints, midpoint, radius."),
  face_filter: z.record(z.string(), z.unknown()).optional().describe("Only list faces matching these selector filters."),
  edge_filter: z.record(z.string(), z.unknown()).optional().describe("Only list edges matching these selector filters."),
  max_items: z.number().int().positive().optional().describe("Cap per list (default 200)."),
});

export const renderViewSchema = z.object({
  path: partPathField,
  output_path: z.string().min(1).describe("PNG/JPG file under an allowed root."),
  view: z
    .enum(["isometric", "trimetric", "dimetric", "front", "back", "top", "bottom", "left", "right", "current"])
    .optional()
    .describe("Default isometric."),
  width: z.number().int().min(64).max(4096).optional(),
  height: z.number().int().min(64).max(4096).optional(),
  edges: z.boolean().optional().describe("Shaded with visible edges (default true)."),
  units: unitsField,
  bodies: bodyList.optional().describe("Show only these bodies in the image (others are hidden temporarily)."),
});

export const deleteBodySchema = z.object({
  path: partPathField,
  units: unitsField,
  bodies: bodyList,
  name: nameField,
});

export const moveBodySchema = z.object({
  path: partPathField,
  units: unitsField,
  bodies: bodyList,
  translate: point3.optional(),
  rotate: z
    .object({ axis_point: point3, axis_direction: point3, angle_deg: z.number() })
    .optional()
    .describe("Rotation about an axis through axis_point."),
  copy: z.boolean().optional().describe("Keep the original and move a copy."),
  name: nameField,
});

// ---------------------------------------------------------------- design introspection / analysis

export const featureDetailsSchema = z.object({
  path: partPathField,
  units: unitsField,
  features: z.array(z.string().min(1)).optional().describe("Only these features (default: the whole tree)."),
  include_sketches: z.boolean().optional().describe("Include sketch geometry (default true)."),
  max_entities: z.number().int().positive().optional().describe("Cap sketch entities per sketch (default 200)."),
  profile: z.boolean().optional().describe("Diagnostics: include per-feature timing (ms) of each lookup."),
});

export const setDimensionsSchema = z.object({
  path: partPathField,
  units: unitsField,
  values: z
    .record(z.string(), z.number())
    .describe("Map of full dimension names to new values, e.g. {\"D1@Boss-Extrude1\": 8, \"D2@Sketch1\": 30}. Lengths in `units`, angles in degrees. Names come from solidworks_feature_details."),
  rollback_on_error: rollbackField,
});

export const printCheckSchema = z.object({
  path: partPathField,
  units: unitsField,
  material: z.string().optional().describe("Filament for mass: PLA (default), PETG, ABS, ASA, TPU, PA/NYLON, PA-CF, PETG-CF, PC."),
  density_g_cm3: z.number().positive().optional().describe("Density for unknown materials."),
  overhang_deg: z.number().min(0).max(89).optional().describe("Max printable overhang from vertical (default 45)."),
  bed: z.array(z.number().positive()).length(3).optional().describe("Printer build volume [x, y, z] in `units` (default 256x256x256 mm, Bambu X1/P1)."),
  up: z.array(z.number()).length(3).optional().describe("Also evaluate this exact build direction (model vector pointing up)."),
});

export const simulateStaticSchema = z.object({
  path: partPathField,
  units: unitsField,
  material: z
    .string()
    .optional()
    .describe("Built-in: PLA (default), PETG, ABS, ASA, PC, PA-CF, PETG-CF, 6061-T6, 7075-T6, STEEL, STAINLESS; or a SOLIDWORKS library material name (e.g. \"AISI 1020\")."),
  custom_material: z
    .object({ name: z.string().optional(), youngs_modulus_mpa: z.number().positive(), poisson: z.number().min(0).max(0.5).optional(), yield_mpa: z.number().positive(), density_kg_m3: z.number().positive().optional() })
    .optional(),
  fixtures: z
    .array(z.object({ type: z.enum(["fixed", "immovable", "roller", "hinge"]).optional(), faces: selectorList }))
    .min(1)
    .describe("Supports. fixed = all DOF; roller = slides in-plane; hinge = rotates about a cylindrical face's axis."),
  loads: z
    .array(
      z.object({
        type: z.enum(["force", "pressure"]).optional(),
        faces: selectorList,
        value_n: z.number().optional().describe("Force magnitude in newtons (total over the faces)."),
        direction: z.array(z.number()).length(3).optional().describe("Model-space force direction; omit for a force normal to (into) the faces."),
        value_mpa: z.number().optional().describe("Pressure in MPa (N/mm^2), normal to the faces."),
      }),
    )
    .min(1),
  gravity: z.boolean().optional().describe("Add self-weight along -Y."),
  mesh_quality: z.enum(["draft", "high"]).optional(),
  element_size: z.number().positive().optional().describe("Global element size in `units` (default: SOLIDWORKS default)."),
  bodies: bodyList
    .optional()
    .describe("Analyze only these bodies (e.g. the bracket in a part that also contains an imported actuator). Others are excluded via a temporary Keep Body feature that is removed afterwards."),
  keep_study: z.boolean().optional().describe("Leave the study in the part for inspection in the UI (default: delete it)."),
  study_name: z.string().optional(),
});


export const rollbackSchema = z.object({
  path: partPathField,
  units: unitsField,
  before: z.string().optional().describe("Roll back to just before this feature (new features are inserted there)."),
  after: z.string().optional().describe("Roll back to just after this feature."),
});

export const reorderFeatureSchema = z.object({
  path: partPathField,
  units: unitsField,
  feature: z.string().min(1),
  before: z.string().optional(),
  after: z.string().optional(),
});

export const equationsSchema = z.object({
  path: partPathField,
  units: unitsField,
  globals: z
    .record(z.string(), z.union([z.number(), z.string()]))
    .optional()
    .describe('Global variables: {"Wall": 2.4} (number in `units`) or {"Wall": "2.4mm"}. Existing ones are updated.'),
  links: z
    .record(z.string(), z.string())
    .optional()
    .describe('Drive dimensions/variables by expressions, e.g. {"D1@Boss-Extrude1": "\\"Wall\\" * 2"} (quote variable names).'),
  remove: z.array(z.string()).optional().describe("Delete equations by left-hand-side name."),
});

export const ribSchema = z.object({
  path: partPathField,
  units: unitsField,
  sketch: z.string().min(1).describe("Open line/chain sketch, usually on a plane through the part."),
  thickness: z.number().positive(),
  side: z.enum(["both", "one"]).optional().describe("Thickness on both sides of the line (default) or one side."),
  flip_side: z.boolean().optional().describe("One-sided ribs: put the thickness on the other side."),
  direction: z.enum(["parallel", "normal"]).optional().describe("Extend parallel to the sketch plane (default, classic web rib) or normal to it."),
  flip_material: z.boolean().optional(),
  draft_deg: z.number().min(0).max(30).optional(),
  draft_outward: z.boolean().optional(),
  name: nameField,
  rollback_on_error: rollbackField,
});

export const draftSchema = z.object({
  path: partPathField,
  units: unitsField,
  neutral: selectorSchema.describe("Neutral plane / face (pull direction)."),
  faces: selectorList.describe("Faces to draft."),
  angle_deg: z.number().positive().max(45),
  flip: z.boolean().optional(),
  propagate: z
    .enum(["tangent", "none", "all_neutral", "inner_loops", "outer_loop"])
    .optional()
    .describe("Face propagation (default tangent, so filleted walls draft together)."),
  name: nameField,
  rollback_on_error: rollbackField,
});

export const helixSchema = z.object({
  path: partPathField,
  units: unitsField,
  on: selectorSchema.describe("Plane or planar face for the base circle."),
  center: point.optional().describe('Base circle center (sketch coords, or model with space:"model").'),
  space: z.enum(["sketch", "model"]).optional(),
  diameter: z.number().positive(),
  pitch: z.number().positive(),
  height: z.number().positive().optional().describe("Height (with pitch). Or give revolutions."),
  revolutions: z.number().positive().optional(),
  clockwise: z.boolean().optional(),
  reverse: z.boolean().optional(),
  start_angle_deg: z.number().optional(),
  taper_deg: z.number().optional(),
  taper_outward: z.boolean().optional(),
  name: nameField,
  rollback_on_error: rollbackField,
});

export const insertPartSchema = z.object({
  path: partPathField,
  units: unitsField,
  source: z.string().min(1).describe("Part file whose bodies are inserted (e.g. a vendor actuator) to design around or subtract."),
  configuration: z.string().optional(),
  import_planes: z.boolean().optional(),
  import_axes: z.boolean().optional(),
  rotate: z.object({ axis_point: point3, axis_direction: point3, angle_deg: z.number() }).optional(),
  translate: point3.optional(),
  name: nameField,
});

export const splitBodySchema = z.object({
  path: partPathField,
  units: unitsField,
  tool: selectorSchema.describe("Plane, planar face, or sketch that cuts through the body."),
  consume_tool: z.boolean().optional(),
  name: nameField,
  rollback_on_error: rollbackField,
});

export const combineSchema = z.object({
  path: partPathField,
  units: unitsField,
  operation: z.enum(["add", "subtract", "common"]),
  target: bodyList.describe("The body to keep/modify (one)."),
  tools: bodyList.describe("Bodies added to / subtracted from / intersected with the target."),
  name: nameField,
  rollback_on_error: rollbackField,
});

export const holeWizardSchema = z.object({
  path: partPathField,
  units: unitsField,
  type: z
    .enum(["tapped", "counterbore", "countersink", "clearance"])
    .optional()
    .describe("tapped (default) = ISO tapped hole with cosmetic thread; counterbore = ISO socket head cap screw; countersink = ISO flat head; clearance = ISO 273 normal fit. For dowel/press-fit holes use solidworks_hole with an exact diameter."),
  size: z.string().optional().describe("ISO size, e.g. M3, M4, M5 (tapped also accepts M3x0.5)."),
  on: selectorSchema.describe("Planar face the holes start on."),
  positions: z.array(point3).min(1).describe("Hole centers as model points on that face."),
  depth: z.number().positive().optional().describe("Drill depth; omit for through-all."),
  thread_depth: z.number().positive().optional(),
  cosmetic_thread: z.boolean().optional(),
  name: nameField,
  rollback_on_error: rollbackField,
});

export const thicknessCheckSchema = z.object({
  path: partPathField,
  units: unitsField,
  min_wall: z.number().positive().optional().describe("Flag walls thinner than this (default 1.2 mm = 3 perimeters of a 0.4 mm nozzle)."),
  samples: z.number().int().positive().optional().describe("Max surface samples (default 3000)."),
});
