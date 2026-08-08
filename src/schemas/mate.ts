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

/**
 * Strict reusable hinge builder: caller-supplied axis/mount/angle refs, explicit
 * LimitAngle branch (align + flip), release listed grounding mates, then prove
 * force-rebuild health plus optional clearance samples. Restores the pre-change
 * checkpoint on any failed stage. Does not save — use confirm_and_save after
 * visual approval.
 */
export const createHingeLimitSchema = z.object({
  path: z.string().min(1).describe("Assembly path under an allowed CAD root."),
  fixed_component: z.string().min(1).describe("Stationary / grounded component name."),
  moving_component: z.string().min(1).describe("Component that rotates about the hinge."),
  fixed_axis_ref: z.string().min(1).describe("Axis (or coaxial cylindrical ref) on the fixed component."),
  moving_axis_ref: z.string().min(1).describe("Axis (or coaxial cylindrical ref) on the moving component."),
  fixed_mount_plane_ref: z.string().min(1).describe("Mounting plane / face on the fixed component."),
  moving_mount_plane_ref: z.string().min(1).describe("Mounting plane / face on the moving component."),
  fixed_angle_plane_ref: z.string().min(1).describe("Angle-measurement plane on the fixed component."),
  moving_angle_plane_ref: z.string().min(1).describe("Angle-measurement plane on the moving component."),
  min_angle_deg: z.number().describe("Minimum LimitAngle bound in degrees."),
  max_angle_deg: z.number().describe("Maximum LimitAngle bound in degrees."),
  park_angle_deg: z.number().describe("Initial / parked LimitAngle value in degrees."),
  align: z.enum(["aligned", "anti_aligned"]).describe("Explicit LimitAngle mate alignment branch."),
  flip_dimension: z.boolean().describe("Explicit LimitAngle FlipDimension branch."),
  release_mates: z
    .array(z.string().min(1))
    .min(1)
    .describe("Legacy grounding mates to suppress before building the hinge (retained for rollback via checkpoint)."),
  axis_align: z
    .enum(["aligned", "anti_aligned"])
    .optional()
    .describe("Optional coaxial axis-mate alignment. Defaults to aligned."),
  mount_align: z
    .enum(["aligned", "anti_aligned"])
    .optional()
    .describe("Optional mounting-plane mate alignment. Defaults to aligned."),
  probe_angles_deg: z
    .array(z.number())
    .optional()
    .describe("Optional angles to drive after construction (defaults to park, min, mid, max)."),
  clearance_component: z
    .string()
    .min(1)
    .optional()
    .describe("Optional component whose bounding-box center must stay on the clear side."),
  clearance_axis: z
    .enum(["x", "y", "z"])
    .optional()
    .describe("Assembly axis used for clearance_component center checks. Defaults to y."),
  clearance_max: z
    .number()
    .optional()
    .describe("Fail if clearance_component center on clearance_axis is greater than this (meters)."),
  clearance_min: z
    .number()
    .optional()
    .describe("Fail if clearance_component center on clearance_axis is less than this (meters)."),
}).refine(
  (data) => data.clearance_component == null
    || data.clearance_max != null
    || data.clearance_min != null,
  {
    message: "When clearance_component is set, provide clearance_max and/or clearance_min.",
  },
).describe(
  "Build a strict coaxial + mount + axis-backed LimitAngle hinge, validate motion/clearance, and restore the checkpoint on failure.",
);

export const mateTrySchema = mateRefsWithSelection
  .extend({
    rebuild: z.boolean().optional(),
    distance_m: z.number().optional(),
  })
  .refine(mateRefsRefine, {
    message: "Provide component/ref names or set use_selection: true with two highlights.",
  });
