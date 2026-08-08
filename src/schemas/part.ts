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

export const featureExtrudeBossSchema = optionalPathSchema.extend({
  depth_m: z.number().optional(),
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
