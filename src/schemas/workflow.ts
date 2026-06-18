import { z } from "zod";

import { confirmField } from "./shared.js";

export const placeShoulderRollMotorsSchema = z.object({
  path: z.string().min(1),
  layout_component: z.string().min(1).optional(),
  left_component: z.string().min(1).optional(),
  right_component: z.string().min(1).optional(),
  side: z.enum(["left", "right", "both"]).optional(),
  save: z.boolean().optional(),
  use_golden: z.boolean().optional(),
  inner_rail_mm: z.number().optional(),
  inner_flange_mm: z.number().optional(),
  ...confirmField,
});

export const buildTorsoComputeShelfSchema = z.object({
  path: z.string().min(1),
  part_path: z.string().min(1).optional(),
  shelf_component: z.string().min(1).optional(),
  layout_component: z.string().min(1).optional(),
  left_actuator: z.string().min(1).optional(),
  right_actuator: z.string().min(1).optional(),
  thickness_mm: z.number().optional(),
  clearance_mm: z.number().optional(),
  mate_in_assembly: z.boolean().optional(),
  save: z.boolean().optional(),
  owner: z.string().min(1).optional(),
  ...confirmField,
});

export const captureShoulderRollGoldenSchema = z.object({
  path: z.string().min(1),
  left_component: z.string().min(1).optional(),
  right_component: z.string().min(1).optional(),
});

export const applyShoulderRollGoldenSchema = z.object({
  path: z.string().min(1),
  left: z.record(z.string(), z.unknown()).optional(),
  right: z.record(z.string(), z.unknown()).optional(),
  save: z.boolean().optional(),
  fix: z.boolean().optional(),
  ...confirmField,
});

export const cutActuatorCavitySchema = z.object({
  path: z.string().min(1).optional(),
  bracket_part_path: z.string().min(1).optional(),
  tool_part_path: z.string().min(1).optional(),
  bracket_component: z.string().min(1).optional(),
  tool_component: z.string().min(1).optional(),
  model: z.enum(["rs00", "rs02", "rs03", "rs04", "rs05"]).optional(),
  clearance_mm: z.number().optional(),
  save: z.boolean().optional(),
  use_selection: z.boolean().optional(),
  selection_index: z.number().int().min(1).optional(),
  ...confirmField,
});

export const vendorAddRs03UrdfFrameSchema = z.object({
  path: z.string().min(1),
  component_name: z.string().min(1).optional(),
  save: z.boolean().optional(),
  ...confirmField,
});

export const createMalletMountSchema = z.object({
  part_path: z.string().min(1),
  handle_diameter_mm: z.number().optional(),
  save: z.boolean().optional(),
  owner: z.string().min(1).optional(),
  ...confirmField,
});

export const layoutAddShoulderMountsSchema = z.object({
  path: z.string().min(1),
  save: z.boolean().optional(),
  replace_existing: z.boolean().optional(),
  inner_rail_mm: z.number().optional(),
  outer_poke_mm: z.number().optional(),
  depth_offset_mm: z.number().optional(),
  ...confirmField,
});
