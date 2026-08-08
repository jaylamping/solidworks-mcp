import { z } from "zod";

export const selectionFieldsSchema = z.object({
  use_selection: z.boolean().optional().describe("Use the current SolidWorks selection instead of named references."),
  selection_index: z.number().int().min(1).optional().describe("1-based selection index when selecting a specific highlighted entity."),
});

export const optionalPathSchema = selectionFieldsSchema.extend({
  path: z.string().min(1).optional().describe("Optional document path under an allowed CAD root."),
});

export const openSchema = z.object({
  path: z.string().min(1).describe("SolidWorks or STEP document path under an allowed CAD root."),
  start_if_missing: z.boolean().optional().describe("Start SolidWorks if no instance is running."),
});

export const exportSchema = z.object({
  path: z.string().min(1).optional().describe("Optional source document path under an allowed CAD root."),
  output_path: z.string().min(1).describe("Destination file path under an allowed CAD root."),
  format: z.enum(["sldprt", "sldasm", "step", "stp", "stl", "pdf", "png"]).describe("Output format."),
  start_if_missing: z.boolean().optional().describe("Start SolidWorks if no instance is running."),
});

export const confirmPathSchema = selectionFieldsSchema.extend({
  path: z.string().min(1).optional().describe("Optional document path under an allowed CAD root."),
  confirm: z.literal(true).describe("Acknowledge the requested state-changing operation."),
});

export const checkpointSchema = z.object({
  path: z.string().min(1),
  force: z.boolean().optional(),
});

export const listCheckpointsSchema = z.object({
  path: z.string().min(1),
});

export const restoreFromCheckpointSchema = z.object({
  checkpoint_path: z.string().min(1),
  path: z.string().min(1).optional(),
  close_open_document: z.boolean().optional(),
  confirm: z.literal(true),
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

export const renameFeatureSchema = z.object({
  path: z.string().min(1).optional(),
  from_name: z.string().min(1),
  to_name: z.string().min(1),
  save: z.boolean().default(false),
  confirm: z.literal(true),
});

export const setMassOverrideSchema = z.object({
  path: z.string().min(1),
  mass_kg: z.number().positive(),
  save: z.boolean().default(false),
  confirm: z.literal(true),
});

export const saveDocumentSchema = optionalPathSchema.extend({
  skip_mate_validation: z.boolean().optional(),
});

export const confirmAndSaveSchema = z.object({
  path: z.string().min(1),
  looks_good: z.literal(true),
  confirm: z.literal(true),
  preview_path: z.string().min(1).optional(),
  reopen: z.boolean().optional(),
  pose_tolerance: z.number().positive().optional(),
});
