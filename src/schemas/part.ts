import { z } from "zod";

import { optionalPathSchema, selectionFieldsSchema } from "./document.js";
import { confirmField } from "./shared.js";

export const roundSideArmsFromCircleSchema = selectionFieldsSchema.extend({
  path: z.string().min(1).optional(),
  plane_name: z.string().min(1).optional(),
  center_x_m: z.number().optional(),
  center_y_m: z.number().optional(),
  radius_m: z.number().positive().optional(),
  samples: z.number().int().min(8).max(96).optional(),
  dry_run: z.boolean().optional(),
  save: z.boolean().optional(),
  ...confirmField,
});

export const createSketchSchema = optionalPathSchema.extend({
  plane_name: z.string().min(1).optional(),
  ...confirmField,
});

export const sketchLineSchema = optionalPathSchema.extend({
  x1_m: z.number().optional(),
  y1_m: z.number().optional(),
  x2_m: z.number().optional(),
  y2_m: z.number().optional(),
});

export const sketchCircleSchema = optionalPathSchema.extend({
  center_x_m: z.number().optional(),
  center_y_m: z.number().optional(),
  radius_m: z.number().optional(),
});

export const sketchRectangleSchema = optionalPathSchema.extend({
  x1_m: z.number().optional(),
  y1_m: z.number().optional(),
  x2_m: z.number().optional(),
  y2_m: z.number().optional(),
  ...confirmField,
});

export const featureExtrudeCutSchema = optionalPathSchema.extend({
  depth_m: z.number().optional(),
  through_all: z.boolean().optional(),
  ...confirmField,
});

export const combineBodiesSchema = optionalPathSchema
  .extend({
    operation: z
      .enum(["common", "add", "subtract"])
      .describe(
        "SolidWorks Combine op. common keeps only the intersection and may be synthesized with subtract for exactly two bodies when InsertCombineFeature cannot create it, add is union, subtract is main minus tools.",
      ),
    body_name: z
      .string()
      .min(1)
      .optional()
      .describe(
        "Primary/target body name. body_names[0] and this field are the main body for subtract and common. Filled from selection index 1 when use_selection is true.",
      ),
    tool_body_name: z
      .string()
      .min(1)
      .optional()
      .describe("Second/tool body name. Filled from selection index 2 when use_selection is true."),
    body_names: z
      .array(z.string().min(1))
      .min(1)
      .optional()
      .describe(
        "Solid body names to combine. body_names[0] is the target/main body for subtract and common; the rest are tools. Prefer body_names over feature_names, especially for early Loft/Shell features.",
      ),
    feature_names: z
      .array(z.string().min(1))
      .min(1)
      .optional()
      .describe("Feature names resolved to owning solid bodies and appended to the body list. Use body_names when possible."),
    keep_body_names: z
      .array(z.string().min(1))
      .optional()
      .describe(
        "Bodies to preserve by copying before combine. Use when common/subtract would otherwise consume a body you still need (e.g. keep Loft body while trimming another body to it).",
      ),
    keep_feature_names: z
      .array(z.string().min(1))
      .optional()
      .describe("Features resolved to bodies and treated like keep_body_names."),
    ...confirmField,
  })
  .superRefine((value, ctx) => {
    const named =
      (value.body_name ? 1 : 0)
      + (value.tool_body_name ? 1 : 0)
      + (value.body_names?.length ?? 0)
      + (value.feature_names?.length ?? 0);
    if (named < 2 && !value.use_selection) {
      ctx.addIssue({
        code: "custom",
        message:
          "Provide at least two body_name/tool_body_name/body_names/feature_names entries, or set use_selection: true to bind two highlighted bodies.",
      });
    }
  });

export const probePartFeatureGeometrySchema = optionalPathSchema.extend({
  feature_name: z.string().min(1),
});

export const setSketchCircleDiameterSchema = optionalPathSchema
  .extend({
    sketch_name: z.string().min(1),
    diameter_m: z.number().optional(),
    diameter_mm: z.number().optional(),
    delta_m: z.number().optional(),
    delta_mm: z.number().optional(),
    match_diameter_m: z.number().optional(),
    match_diameter_mm: z.number().optional(),
    prefer_inner: z.boolean().optional(),
    ...confirmField,
  })
  .superRefine((value, ctx) => {
    if (
      value.diameter_m === undefined
      && value.diameter_mm === undefined
      && value.delta_m === undefined
      && value.delta_mm === undefined
    ) {
      ctx.addIssue({
        code: "custom",
        message: "Provide diameter_m/diameter_mm or delta_m/delta_mm.",
      });
    }
  });

export const featureExtrudeBossSchema = optionalPathSchema.extend({
  depth_m: z.number().optional(),
  merge: z
    .boolean()
    .optional()
    .describe("When false, create a separate solid body (tool bodies, envelopes). Default true."),
  flip: z.boolean().optional(),
  merge_body_name: z
    .string()
    .min(1)
    .optional()
    .describe(
      "When merge is true, append-select this solid so FeatureExtrusion does not auto-consume other multi-body solids.",
    ),
  use_feat_scope: z.boolean().optional(),
  use_auto_select: z
    .boolean()
    .optional()
    .describe("Defaults to merge && !merge_body_name. Set false with merge:false for reliable tool bodies."),
  sketch_name: z.string().min(1).optional(),
  ...confirmField,
});

export const deleteFeatureSchema = optionalPathSchema.extend({
  feature_name: z.string().min(1),
  ...confirmField,
});

export const featureFilletSchema = optionalPathSchema.extend({
  radius_m: z.number().optional(),
  radius_mm: z.number().optional(),
  ...confirmField,
});

export const featureChamferSchema = optionalPathSchema.extend({
  distance_m: z.number().optional(),
  distance_mm: z.number().optional(),
  ...confirmField,
});

export const featureMirrorSchema = optionalPathSchema.extend({
  feature_name: z.string().min(1),
  plane_name: z.string().min(1).optional(),
  ...confirmField,
});

export const featureLinearPatternSchema = optionalPathSchema.extend({
  feature_name: z.string().min(1),
  count: z.number().int().min(2).optional(),
  spacing_m: z.number().optional(),
  spacing_mm: z.number().optional(),
  direction: z.enum(["X", "Y"]).optional(),
  ...confirmField,
});

export const featureCircularPatternSchema = optionalPathSchema.extend({
  feature_name: z.string().min(1),
  count: z.number().int().min(2).optional(),
  angle_deg: z.number().optional(),
  axis_name: z.string().min(1).optional(),
  ...confirmField,
});

export const setMaterialSchema = optionalPathSchema.extend({
  material: z.string().min(1),
  database: z.string().min(1).optional(),
  ...confirmField,
});

export const newDocumentSchema = z.object({
  doc_type: z.enum(["part", "assembly", "drawing"]).optional(),
  output_path: z.string().min(1).optional(),
  ...confirmField,
});

export const demoBuildPartSchema = z.object({
  size_mm: z.number().positive().default(40).describe("Cube edge length in millimeters."),
  hole_diameter_mm: z
    .number()
    .positive()
    .default(18)
    .describe("Diameter of the through-cylinders cut on each axis. Must be smaller than size_mm."),
  output_path: z.string().min(1).optional(),
  ...confirmField,
});

export const createSubassemblySchema = z.object({
  output_path: z.string().min(1),
  component_path: z.string().min(1).optional(),
  ...confirmField,
});

export const createDrawingFromModelSchema = z.object({
  model_path: z.string().min(1),
  output_path: z.string().min(1),
  ...confirmField,
});

export const addStandardViewsSchema = optionalPathSchema.extend({
  sheet_name: z.string().min(1).optional(),
});

export const addConfigurationCopySchema = z.object({
  path: z.string().min(1),
  from: z.string().min(1),
  to: z.string().min(1),
});

export const ensureOffsetPlaneSchema = z.object({
  part_path: z.string().min(1),
  plane_name: z.string().min(1),
  offset_m: z.number(),
  reference_plane: z.string().min(1).optional(),
  replace_existing: z.boolean().optional(),
  save: z.boolean().optional(),
});

export const packAndGoSchema = z.object({
  path: z.string().min(1),
  output_dir: z.string().min(1),
});
