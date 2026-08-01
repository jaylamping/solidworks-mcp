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
    axis_ref: z.string().min(1).optional().describe("Optional rotation axis reference."),
    axis_component: z.string().min(1).optional().describe("Component containing the rotation axis."),
    seed_angle_deg: z.number().optional().describe(
      "Current/nominal angle in degrees. Prefer the live pose angle so creation does not yank the joint to the range midpoint.",
    ),
    flip: z.boolean().optional().describe("FlipDimension sense for the limit-angle mate."),
    check_branch_stability: z.boolean().optional().describe(
      "If true (default), suppress/unsuppress the new mate and detect opposite-branch pose flips common with Top/Top planar angles.",
    ),
    auto_stable_planes: z.boolean().optional().describe(
      "If true (default), rewrite Top/Top to Front/Front before create, and recreate if a branch flip is still detected.",
    ),
  })
  .refine(mateRefsRefine, {
    message: "Provide component/ref names or set use_selection: true with two highlights.",
  }).describe(
    "Create a limit-angle mate between two references. Prefer Front/Front (or Right/Right) over Top/Top for revolute joints — Top/Top is often ambiguous and can jump sides on save/rebuild.",
  );

export const setMateLimitAngleSchema = z.object({
  path: z.string().min(1).describe("Assembly path under an allowed CAD root."),
  mate_name: z.string().min(1).describe("Existing LimitAngle mate feature name, e.g. LimitAngle9."),
  min_angle_deg: z.number().optional().describe("New minimum allowed rotation in degrees."),
  max_angle_deg: z.number().optional().describe("New maximum allowed rotation in degrees."),
  angle_deg: z.number().optional().describe("New current/nominal angle in degrees."),
  flip: z.boolean().optional().describe("Optional FlipDimension override."),
  component_1: z.string().min(1).optional().describe("First component name. Required to recreate if in-place edit fails."),
  ref_1: z.string().min(1).optional().describe("Reference on the first component for recreate fallback."),
  component_2: z.string().min(1).optional().describe("Second component name for recreate fallback."),
  ref_2: z.string().min(1).optional().describe("Reference on the second component for recreate fallback."),
  axis_ref: z.string().min(1).optional().describe("Optional rotation axis reference for recreate fallback."),
  axis_component: z.string().min(1).optional().describe("Component containing the rotation axis for recreate fallback."),
  seed_angle_deg: z.number().optional().describe("Seed angle for recreate fallback (defaults to angle_deg)."),
  check_branch_stability: z.boolean().optional().describe(
    "If true (default), suppress/unsuppress after update to detect opposite-branch pose flips.",
  ),
}).refine(
  (data) =>
    data.min_angle_deg != null
    || data.max_angle_deg != null
    || data.angle_deg != null
    || data.flip != null,
  { message: "Provide at least one of min_angle_deg, max_angle_deg, angle_deg, or flip." },
).describe("Update min/max/current angle on an existing limit-angle mate; recreates when in-place edit fails and component refs are provided.");

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
