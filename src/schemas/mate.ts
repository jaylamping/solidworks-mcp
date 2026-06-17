import { z } from "zod";

import { selectionFieldsSchema } from "./document.js";

const mateRefsBaseSchema = z.object({
  path: z.string().min(1).optional(),
  component_1: z.string().min(1).optional(),
  ref_1: z.string().min(1).optional(),
  component_2: z.string().min(1).optional(),
  ref_2: z.string().min(1).optional(),
  face_index_1: z.number().int().optional(),
  face_index_2: z.number().int().optional(),
  align: z.enum(["aligned", "anti_aligned"]).optional(),
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
});

export const mateLimitAngleSchema = mateRefsWithSelection
  .extend({
    min_angle_deg: z.number().optional(),
    max_angle_deg: z.number().optional(),
  })
  .refine(mateRefsRefine, {
    message: "Provide component/ref names or set use_selection: true with two highlights.",
  });

export const mateTrySchema = mateRefsWithSelection
  .extend({
    rebuild: z.boolean().optional(),
    distance_m: z.number().optional(),
  })
  .refine(mateRefsRefine, {
    message: "Provide component/ref names or set use_selection: true with two highlights.",
  });
