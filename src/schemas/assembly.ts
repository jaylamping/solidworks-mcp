import { z } from "zod";

import { confirmPathSchema, optionalPathSchema, selectionFieldsSchema } from "./document.js";
import { confirmField } from "./shared.js";

export {
  mateLimitAngleSchema,
  mateRefsSchema,
  mateTrySchema,
  probeAngleTravelSchema,
} from "./mate.js";

export const componentNameSchema = optionalPathSchema.extend({
  component_name: z.string().min(1).optional(),
}).refine((data) => data.use_selection || Boolean(data.component_name), {
  message: "Provide component_name or set use_selection: true.",
});

export const transformComponentSchema = z.object({
  path: z.string().min(1),
  component_name: z.string().min(1),
  tx: z.number().optional(),
  ty: z.number().optional(),
  tz: z.number().optional(),
}).merge(selectionFieldsSchema);

export const setComponentTransformSchema = z.object({
  path: z.string().min(1),
  component_name: z.string().min(1),
  matrix: z.array(z.number()).length(16),
  fix: z.boolean().optional(),
}).merge(selectionFieldsSchema);

export const setDimensionSchema = z.object({
  path: z.string().min(1),
  dimension: z.string().min(1),
  value_meters: z.number(),
  configuration: z.string().min(1).optional(),
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

export const selectByPersistReferenceSchema = z.object({
  path: z.string().min(1),
  persist_reference: z.string().min(1),
  mark: z.number().int().optional(),
  append: z.boolean().optional(),
});

export const insertComponentSchema = z.object({
  path: z.string().min(1),
  part_path: z.string().min(1),
  name: z.string().min(1).optional(),
  configuration: z.string().optional(),
  save: z.boolean().optional(),
});

export const deleteMateSchema = z.object({
  path: z.string().min(1),
  mate_name: z.string().min(1),
  ...confirmField,
});

export const deleteMatesInRangeSchema = z.object({
  path: z.string().min(1).optional(),
  min_number: z.number().int().optional(),
  max_number: z.number().int().optional(),
  save: z.boolean().optional(),
  ...confirmField,
});

export const deleteAllMatesSchema = confirmPathSchema;

export const dissolveComponentSchema = optionalPathSchema.extend({
  component_name: z.string().min(1),
  ...confirmField,
});

export const mirrorComponentSchema = z.object({
  path: z.string().min(1),
  component_name: z.string().min(1),
  mirror_plane: z.string().min(1).optional(),
  ...confirmField,
});

export const copyWithMatesSchema = optionalPathSchema.extend({
  component_name: z.string().min(1),
  ...confirmField,
});

export const explodeViewSchema = confirmPathSchema;

export const setFeatureSuppressionSchema = z.object({
  path: z.string().min(1),
  feature_name: z.string().min(1),
  suppressed: z.boolean().optional(),
  configuration: z.string().min(1).optional(),
});

export const setMateSuppressionSchema = z.object({
  path: z.string().min(1),
  mate_name: z.string().min(1),
  suppressed: z.boolean().optional(),
});

export const setComponentConfigurationSchema = z.object({
  path: z.string().min(1),
  component_name: z.string().min(1),
  configuration: z.string().min(1),
});

export const cloneSolidBodyPartSchema = z.object({
  source_part_path: z.string().min(1),
  output_part_path: z.string().min(1),
  assembly_path: z.string().min(1).optional(),
  component_name: z.string().min(1).optional(),
  save: z.boolean().optional(),
  ...confirmField,
});

export const mirrorPartFileSchema = z.object({
  source_part_path: z.string().min(1),
  output_part_path: z.string().min(1),
  mirror_plane: z.string().min(1).optional(),
  save: z.boolean().optional(),
  ...confirmField,
});

export const makeComponentIndependentSchema = z.object({
  assembly_path: z.string().min(1),
  component_name: z.string().min(1),
  save: z.boolean().optional(),
  ...confirmField,
});

export const replaceComponentsByPathSchema = z.object({
  path: z.string().min(1),
  from_part_path: z.string().min(1),
  to_part_path: z.string().min(1),
  configuration: z.string().min(1).optional(),
  save: z.boolean().optional(),
  ...confirmField,
});

export const replaceComponentPathSchema = z.object({
  path: z.string().min(1),
  component_name: z.string().min(1),
  to_part_path: z.string().min(1),
  configuration: z.string().min(1).optional(),
  save: z.boolean().optional(),
  ...confirmField,
});

export const exportLinkTransformsSchema = z.object({
  path: z.string().min(1),
  output_path: z.string().min(1).optional(),
});

export const measureDistanceSchema = z.object({
  path: z.string().min(1).optional(),
  entity_a: z.string().min(1).optional(),
  entity_b: z.string().min(1).optional(),
}).merge(selectionFieldsSchema);

export const getAssemblyDegreesOfFreedomSchema = optionalPathSchema;
