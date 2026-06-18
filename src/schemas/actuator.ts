import { z } from "zod";

import { optionalPathSchema } from "./document.js";
import { confirmField } from "./shared.js";

const actuatorModelSchema = z.enum(["rs00", "rs02", "rs03", "rs04", "rs05"]);

export const actuatorGetEnvelopeSchema = optionalPathSchema.extend({
  model: actuatorModelSchema.optional(),
  vendor_path: z.string().min(1).optional(),
});

export const actuatorProbeMountFaceSchema = optionalPathSchema.extend({
  model: actuatorModelSchema.optional(),
  plane_name: z.string().min(1).optional(),
});

export const actuatorMountHolePatternSchema = optionalPathSchema.extend({
  model: actuatorModelSchema.optional(),
  plane_name: z.string().min(1).optional(),
  bolt_circle_diameter_mm: z.number().optional(),
  bolt_count: z.number().int().min(1).optional(),
  hole_diameter_mm: z.number().optional(),
  start_angle_deg: z.number().optional(),
  feature_prefix: z.string().min(1).optional(),
  save: z.boolean().optional(),
  ...confirmField,
});

export const actuatorCutCavitySchema = optionalPathSchema.extend({
  bracket_part_path: z.string().min(1).optional(),
  tool_part_path: z.string().min(1).optional(),
  bracket_component: z.string().min(1).optional(),
  tool_component: z.string().min(1).optional(),
  model: actuatorModelSchema.optional(),
  clearance_mm: z.number().optional(),
  save: z.boolean().optional(),
  ...confirmField,
});

export const actuatorInsertVendorSchema = z.object({
  path: z.string().min(1),
  model: actuatorModelSchema.optional(),
  vendor_path: z.string().min(1).optional(),
  component_prefix: z.string().min(1).optional(),
  save: z.boolean().optional(),
  ...confirmField,
});

export const actuatorAddUrdfFrameSchema = optionalPathSchema.extend({
  model: actuatorModelSchema.optional(),
  component_name: z.string().min(1).optional(),
  save: z.boolean().optional(),
  ...confirmField,
});
