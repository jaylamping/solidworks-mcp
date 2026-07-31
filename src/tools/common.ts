import { z } from "zod";

export function jsonResult(data: unknown) {
  return {
    content: [{ type: "text" as const, text: JSON.stringify(data, null, 2) }],
  };
}

export function errorResult(error: unknown) {
  const message = error instanceof Error ? error.message : String(error);
  return {
    content: [{ type: "text" as const, text: message }],
    isError: true as const,
  };
}

export const optionalPathSchema = z.object({
  path: z.string().min(1).optional(),
});

export const confirmSchema = z.object({
  confirm: z.literal(true),
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

export const setCustomPropertiesSchema = z.object({
  path: z.string().min(1),
  properties: z.record(z.string(), z.string()),
  save: z.boolean().optional(),
});
