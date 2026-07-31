import { z } from "zod";

import { optionalPathSchema } from "./document.js";

export const urdfReadinessSchema = optionalPathSchema.extend({
  required_refs: z.array(z.string().min(1)).optional(),
});

export const addUrdfFrameSchema = z.object({
  path: z.string().min(1),
  name: z.string().min(1).optional(),
  origin_x_m: z.number().optional(),
  origin_y_m: z.number().optional(),
  origin_z_m: z.number().optional(),
  x_axis_ref: z.string().min(1).optional(),
  y_axis_ref: z.string().min(1).optional(),
  replace_existing: z.boolean().optional(),
  save: z.boolean().optional(),
  confirm: z.literal(true),
});
