import { z } from "zod";

import { assertAllowedPath } from "./config.js";
import { runWorker } from "./worker.js";

const assemblyPathSchema = z.object({
  path: z.string().min(1),
});

const mateRefsSchema = assemblyPathSchema.extend({
  component_1: z.string().min(1),
  ref_1: z.string().min(1),
  component_2: z.string().min(1),
  ref_2: z.string().min(1),
  face_index_1: z.number().int().nonnegative().optional(),
  face_index_2: z.number().int().nonnegative().optional(),
});

const featureProbeSchema = assemblyPathSchema.extend({
  component_name: z.string().min(1),
  feature_name: z.string().min(1),
});

const alignSchema = assemblyPathSchema.extend({
  layout_component: z.string().min(1),
  layout_feature: z.string().min(1),
  target_component: z.string().min(1),
  target_plane: z.string().min(1).optional(),
});

const torsoFrameBuildSchema = assemblyPathSchema.extend({
  /** Must be true — destructive layout snap; only call when the user explicitly asked. */
  confirm: z.literal(true),
  include_brackets: z.boolean().optional(),
  rebuild_configs: z.boolean().optional(),
});

export const assemblyToolSchemas = {
  assemblyPathSchema,
  mateRefsSchema,
  featureProbeSchema,
  alignSchema,
  torsoFrameBuildSchema,
};

export async function listMates(path: string) {
  return runWorker({ command: "list_mates", args: { path } });
}

export async function saveDocument(path: string) {
  return runWorker({ command: "save_document", args: { path } });
}

export async function probeFeatureFaces(args: z.infer<typeof featureProbeSchema>) {
  const filePath = assertAllowedPath(args.path);
  return runWorker({
    command: "probe_feature_faces",
    args: {
      path: filePath,
      component_name: args.component_name,
      feature_name: args.feature_name,
    },
  });
}

export async function getFeatureBox(args: z.infer<typeof featureProbeSchema>) {
  const filePath = assertAllowedPath(args.path);
  return runWorker({
    command: "get_feature_box",
    args: {
      path: filePath,
      component_name: args.component_name,
      feature_name: args.feature_name,
    },
  });
}

export async function alignComponentToFeature(args: z.infer<typeof alignSchema>) {
  const filePath = assertAllowedPath(args.path);
  return runWorker({
    command: "align_component_to_feature",
    args: {
      path: filePath,
      layout_component: args.layout_component,
      layout_feature: args.layout_feature,
      target_component: args.target_component,
      target_plane: args.target_plane ?? "Plane1",
    },
  });
}

export async function mateCoincident(args: z.infer<typeof mateRefsSchema>) {
  const filePath = assertAllowedPath(args.path);
  return runWorker({
    command: "mate_coincident",
    args: { ...args, path: filePath },
  });
}

export async function mateParallel(args: z.infer<typeof mateRefsSchema>) {
  const filePath = assertAllowedPath(args.path);
  return runWorker({
    command: "mate_parallel",
    args: { ...args, path: filePath },
  });
}

export async function mateDistance(
  args: z.infer<typeof mateRefsSchema> & { confirm: true },
) {
  if (args.confirm !== true) {
    throw new Error("Refusing mate_distance: pass confirm: true when the user explicitly requested it.");
  }
  const filePath = assertAllowedPath(args.path);
  return runWorker({
    command: "mate_distance",
    args: { ...args, path: filePath },
  });
}

export async function matePerpendicular(
  args: z.infer<typeof mateRefsSchema> & { confirm: true },
) {
  if (args.confirm !== true) {
    throw new Error("Refusing mate_perpendicular: pass confirm: true when the user explicitly requested it.");
  }
  const filePath = assertAllowedPath(args.path);
  return runWorker({
    command: "mate_perpendicular",
    args: { ...args, path: filePath },
  });
}

export async function torsoFrameBuildMates(args: z.infer<typeof torsoFrameBuildSchema>) {
  if (args.confirm !== true) {
    throw new Error(
      "Refusing torso frame build: pass confirm: true only when the user explicitly requested layout placement.",
    );
  }
  const filePath = assertAllowedPath(args.path);
  return runWorker({
    command: "torso_frame_build_mates",
    args: {
      path: filePath,
      include_brackets: args.include_brackets ?? true,
      rebuild_configs: args.rebuild_configs ?? true,
    },
  });
}
