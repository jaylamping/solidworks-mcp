import { z } from "zod";

export const confirmField = {
  confirm: z.literal(true),
} as const;
