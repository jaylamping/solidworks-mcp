import { z } from "zod";

import { assertAllowedPath } from "./config.js";
import {
  alignSchema,
  featureProbeSchema,
} from "./schemas/assembly.js";
import { mateRefsSchema } from "./schemas/mate.js";
import { runWorker } from "./worker.js";

const assemblyPathSchema = z.object({
  path: z.string().min(1),
});

export const assemblyToolSchemas = {
  assemblyPathSchema,
  mateRefsSchema,
  featureProbeSchema,
  alignSchema,
};

function resolveOptionalPath(path?: string) {
  return path ? assertAllowedPath(path) : undefined;
}

export async function listMates(path: string) {
  return runWorker({ command: "list_mates", args: { path } });
}

export async function saveDocument(path: string) {
  return runWorker({ command: "save_document", args: { path } });
}

export async function probeFeatureFaces(args: z.infer<typeof featureProbeSchema>) {
  const filePath = resolveOptionalPath(args.path);
  return runWorker({
    command: "probe_feature_faces",
    args: { ...args, path: filePath },
  });
}

export async function getFeatureBox(args: z.infer<typeof featureProbeSchema>) {
  const filePath = resolveOptionalPath(args.path);
  return runWorker({
    command: "get_feature_box",
    args: { ...args, path: filePath },
  });
}

export async function alignComponentToFeature(args: z.infer<typeof alignSchema>) {
  const filePath = resolveOptionalPath(args.path);
  return runWorker({
    command: "align_component_to_feature",
    args: {
      ...args,
      path: filePath,
      target_plane: args.target_plane ?? "Plane1",
    },
  });
}

export async function mateCoincident(args: z.infer<typeof mateRefsSchema>) {
  const filePath = resolveOptionalPath(args.path);
  return runWorker({
    command: "mate_coincident",
    args: { ...args, path: filePath },
  });
}

export async function mateParallel(args: z.infer<typeof mateRefsSchema>) {
  const filePath = resolveOptionalPath(args.path);
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
  const filePath = resolveOptionalPath(args.path);
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
  const filePath = resolveOptionalPath(args.path);
  return runWorker({
    command: "mate_perpendicular",
    args: { ...args, path: filePath },
  });
}
