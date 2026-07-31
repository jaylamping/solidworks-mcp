import { z } from "zod";

export const selectionFieldsSchema = z.object({
  use_selection: z.boolean().optional(),
  selection_index: z.number().int().min(1).optional(),
});

export const optionalPathSchema = selectionFieldsSchema.extend({
  path: z.string().min(1).optional(),
});

export const openSchema = z.object({
  path: z.string().min(1),
  start_if_missing: z.boolean().optional(),
});

export const exportSchema = z.object({
  path: z.string().min(1).optional(),
  output_path: z.string().min(1),
  format: z.enum(["sldprt", "sldasm", "step", "stp", "stl", "pdf", "png"]),
  start_if_missing: z.boolean().optional(),
});

export const confirmPathSchema = selectionFieldsSchema.extend({
  path: z.string().min(1).optional(),
  confirm: z.literal(true),
});

export const checkpointSchema = z.object({
  path: z.string().min(1),
});

export const diagnoseSchema = selectionFieldsSchema.extend({
  path: z.string().min(1).optional(),
  start_if_missing: z.boolean().optional(),
});

export const explainErrorSchema = z.object({
  code: z.string().optional(),
  hresult: z.string().optional(),
  sw_error_code: z.number().optional(),
  message: z.string().optional(),
});

export const closeDocumentSchema = z.object({
  path: z.string().min(1),
  save: z.boolean().optional(),
});

export const closeAllDocumentsSchema = z.object({
  save_first: z.boolean().optional(),
  confirm: z.literal(true),
});

export const rebuildDocumentSchema = z.object({
  path: z.string().min(1),
  force: z.boolean().optional(),
});

export const importStepSchema = z.object({
  path: z.string().min(1),
  start_if_missing: z.boolean().optional(),
});

export const diagnosePartSaveSchema = z.object({
  path: z.string().min(1),
});

export const resolveLightweightSchema = confirmPathSchema;

export const unfixAllComponentsSchema = confirmPathSchema;

export const setCustomPropertiesSchema = z.object({
  path: z.string().min(1),
  properties: z.record(z.string(), z.string()),
  save: z.boolean().optional(),
});

export const saveDocumentSchema = optionalPathSchema;
