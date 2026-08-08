import { z } from "zod";

import { selectionFieldsSchema } from "./document.js";

const mateRefsBaseSchema = z.object({
  path: z.string().min(1).optional().describe("Optional assembly path under an allowed CAD root."),
  component_1: z.string().min(1).optional().describe("First component name."),
  ref_1: z.string().min(1).optional().describe("Reference name or feature face on the first component."),
  component_2: z.string().min(1).optional().describe("Second component name."),
  ref_2: z.string().min(1).optional().describe("Reference name or feature face on the second component."),
  face_index_1: z.number().int().optional().describe("Optional face index for the first reference."),
  face_index_2: z.number().int().optional().describe("Optional face index for the second reference."),
  align: z.enum(["aligned", "anti_aligned"]).optional().describe("Reference alignment direction."),
});

function mateRefsRefine(data: z.infer<typeof mateRefsBaseSchema> & z.infer<typeof selectionFieldsSchema>) {
  if (data.use_selection) {
    return true;
  }
  return Boolean(data.component_1 && data.ref_1 && data.component_2 && data.ref_2);
}

const mateRefsWithSelection = mateRefsBaseSchema.merge(selectionFieldsSchema);

export const mateRefsSchema = mateRefsWithSelection.refine(mateRefsRefine, {
  message: "Provide component/ref names or set use_selection: true with two highlights.",
}).describe("Provide two named component references, or use the current selection.");

export const mateLimitAngleSchema = mateRefsWithSelection
  .extend({
    min_angle_deg: z.number().optional().describe("Minimum allowed rotation in degrees."),
    max_angle_deg: z.number().optional().describe("Maximum allowed rotation in degrees."),
    angle_deg: z.number().optional().describe("Current or initial angle in degrees."),
    axis_ref: z.string().min(1).optional().describe("Optional rotation axis reference."),
    axis_component: z.string().min(1).optional().describe("Component containing the rotation axis."),
  })
  .refine(mateRefsRefine, {
    message: "Provide component/ref names or set use_selection: true with two highlights.",
  }).describe("Create a limit-angle mate between two references.");

export const setMateLimitAngleSchema = z.object({
  path: z.string().min(1),
  mate_name: z.string().min(1),
  min_angle_deg: z.number().optional(),
  max_angle_deg: z.number().optional(),
  angle_deg: z.number().optional(),
  flip_dimension: z.boolean().optional(),
}).refine(
  (data) =>
    data.min_angle_deg != null
    || data.max_angle_deg != null
    || data.angle_deg != null
    || data.flip_dimension != null,
  { message: "Provide at least one of min_angle_deg, max_angle_deg, angle_deg, flip_dimension." },
);

export const probeAngleTravelSchema = z.object({
  path: z.string().min(1).describe("Assembly path under an allowed CAD root."),
  component_name: z.string().min(1).describe("Component to move through the angle sweep."),
  reference_component: z.string().min(1).describe("Stationary component used as the angular reference."),
  axis: z.enum(["x", "y", "z"]).optional().describe("Rotation axis."),
  angles_deg: z.array(z.number()).optional().describe("Explicit angles to probe in degrees."),
  min_angle_deg: z.number().optional().describe("Sweep minimum in degrees."),
  max_angle_deg: z.number().optional().describe("Sweep maximum in degrees."),
  overshoot_deg: z.number().optional().describe("Optional overshoot beyond each endpoint."),
  tolerance_deg: z.number().optional().describe("Angular comparison tolerance in degrees."),
  restore: z.boolean().optional().describe("Restore the original component position after probing."),
}).refine(
  (data) =>
    (data.angles_deg != null && data.angles_deg.length > 0)
    || (data.min_angle_deg != null && data.max_angle_deg != null),
  { message: "Provide angles_deg[] or both min_angle_deg and max_angle_deg." },
).describe("Probe angular travel using explicit angles or a minimum/maximum sweep.");

export const mateTrySchema = mateRefsWithSelection
  .extend({
    rebuild: z.boolean().optional(),
    distance_m: z.number().optional(),
  })
  .refine(mateRefsRefine, {
    message: "Provide component/ref names or set use_selection: true with two highlights.",
  });
