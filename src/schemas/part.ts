import { z } from "zod";

import { optionalPathSchema } from "./document.js";

export const sketchRectangleSchema = optionalPathSchema.extend({
  x1_m: z.number().optional(),
  y1_m: z.number().optional(),
  x2_m: z.number().optional(),
  y2_m: z.number().optional(),
});

export const extrudeBossSchema = optionalPathSchema.extend({
  depth_m: z.number().optional(),
});

export const createSketchSchema = optionalPathSchema.extend({
  plane_name: z.string().min(1).optional(),
});

export const deleteFeatureSchema = optionalPathSchema.extend({
  feature_name: z.string().min(1),
});

export const newDocumentSchema = z.object({
  doc_type: z.enum(["part", "assembly", "drawing"]).optional(),
  output_path: z.string().min(1).optional(),
  confirm: z.literal(true).optional(),
});
