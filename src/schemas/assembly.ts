import { z } from "zod";

import { optionalPathSchema, selectionFieldsSchema } from "./document.js";

export { mateLimitAngleSchema, mateRefsSchema, mateTrySchema } from "./mate.js";

export const componentNameSchema = optionalPathSchema.extend({
  component_name: z.string().min(1).optional(),
}).refine((data) => data.use_selection || Boolean(data.component_name), {
  message: "Provide component_name or set use_selection: true.",
});

export const alignSchema = z.object({
  path: z.string().min(1).optional(),
  layout_component: z.string().min(1).optional(),
  layout_feature: z.string().min(1).optional(),
  target_component: z.string().min(1).optional(),
  target_plane: z.string().min(1).optional(),
}).merge(selectionFieldsSchema).refine(
  (data) => data.use_selection || Boolean(data.layout_feature && data.target_component && data.layout_component),
  { message: "Provide align targets or set use_selection: true with two highlights (layout, then target).",
  },
);

export const featureProbeSchema = z.object({
  path: z.string().min(1).optional(),
  component_name: z.string().min(1).optional(),
  feature_name: z.string().min(1).optional(),
}).merge(selectionFieldsSchema).refine(
  (data) => data.use_selection || Boolean(data.component_name && data.feature_name),
  { message: "Provide component/feature names or set use_selection: true.",
  },
);

export const partFeatureProbeSchema = z.object({
  part_path: z.string().min(1).optional(),
  path: z.string().min(1).optional(),
  feature_name: z.string().min(1).optional(),
}).merge(selectionFieldsSchema).refine(
  (data) => data.use_selection || Boolean(data.feature_name && (data.part_path || data.path)),
  { message: "Provide part_path (or path) and feature_name, or set use_selection: true.",
  },
);

export const persistRefSchema = z.object({
  path: z.string().min(1).optional(),
  component_name: z.string().min(1).optional(),
  ref: z.string().min(1).optional(),
  face_index: z.number().int().optional(),
}).merge(selectionFieldsSchema).refine(
  (data) => data.use_selection || Boolean(data.component_name && data.ref),
  { message: "Provide component_name and ref, or set use_selection: true." },
);

export const torsoFrameBuildSchema = z.object({
  path: z.string().min(1),
  confirm: z.literal(true),
  include_brackets: z.boolean().optional(),
  rebuild_configs: z.boolean().optional(),
});
