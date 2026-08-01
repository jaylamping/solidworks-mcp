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
  keep_view: z.boolean().optional().describe(
    "For png/jpg previews: keep the current camera/view instead of forcing isometric + zoom-to-fit. Use this for before/after reasoning screenshots.",
  ),
});

export const confirmPathSchema = selectionFieldsSchema.extend({
  path: z.string().min(1).optional().describe("Optional document path under an allowed CAD root."),
  confirm: z.literal(true).describe("Acknowledge the requested state-changing operation."),
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

export const confirmAndSaveSchema = z.object({
  path: z.string().min(1).optional().describe("Assembly/part path under an allowed CAD root. Defaults to the active document."),
  looks_good: z.literal(true).describe(
    "User visually approved the current viewport. Do not set this until the user answers yes to 'Does this look good?'.",
  ),
  confirm: z.literal(true).describe("Acknowledge the save/heal operation."),
  preview_path: z.string().min(1).optional().describe(
    "Optional PNG path for a keep_view screenshot captured before save.",
  ),
  checkpoint: z.boolean().optional().describe("Create a checkpoint before save (default true)."),
  pose_tolerance: z.number().optional().describe("Max sum of absolute transform deltas allowed after save/rebuild."),
  max_heal_attempts: z.number().int().optional().describe("How many fix/save/restore cycles to try when the pose jumps."),
}).describe(
  "Ask the user 'Does this look good?' first. Only then call with looks_good:true to checkpoint, lock pose, heal warning mates, save, and verify the pose did not jump.",
);
