import { z } from "zod";

import { optionalPathSchema } from "./document.js";

export const urdfReadinessSchema = optionalPathSchema.extend({
  required_refs: z.array(z.string().min(1)).optional(),
});

export const addUrdfFrameSchema = z.object({
  path: z.string().min(1),
  name: z.string().min(1).optional(),
  origin_x_m: z.number().optional(),
  origin_y_m: z.number().optional(),
  origin_z_m: z.number().optional(),
  x_axis_ref: z.string().min(1).optional(),
  y_axis_ref: z.string().min(1).optional(),
  replace_existing: z.boolean().optional(),
  /** Prefer false during CAD prep; confirm_and_save locks the assembly. */
  save: z.boolean().optional(),
  confirm: z.literal(true),
});

const vec3Schema = z.tuple([z.number(), z.number(), z.number()]);

const poseSchema = z.object({
  xyz: vec3Schema,
  rpy: vec3Schema,
});

const inertiaSchema = z.object({
  ixx: z.number(),
  ixy: z.number(),
  ixz: z.number(),
  iyy: z.number(),
  iyz: z.number(),
  izz: z.number(),
});

export const urdfLinkManifestSchema = z.object({
  linkName: z.string().min(1),
  bodies: z.array(z.string().min(1)).min(1),
  frameRef: z.string().min(1).optional(),
  frameBody: z.string().min(1).optional(),
  massOverrideKg: z.number().positive().optional(),
});

const urdfJointSchema = z.object({
  jointName: z.string().min(1),
  type: z.enum(["revolute", "continuous", "fixed"]).default("revolute"),
  parent: z.string().min(1),
  child: z.string().min(1),
  axisRef: z.string().min(1).optional(),
  axisBody: z.string().min(1).optional(),
  limitMate: z.string().min(1).optional(),
  effort: z.number().nonnegative().optional(),
  velocity: z.number().nonnegative().optional(),
  axisSign: z.union([z.literal(1), z.literal(-1)]).optional(),
  limitLower: z.number().optional(),
  limitUpper: z.number().optional(),
  limitSign: z.union([z.literal(1), z.literal(-1)]).optional(),
});

const urdfJointManifestFileBaseSchema = z.object({
  schemaVersion: z.literal(1),
  robotName: z.string().min(1),
  assemblyPath: z.string().min(1),
  packageRoot: z.string().min(1),
  urdfOutputPath: z.string().min(1),
  zeroConfiguration: z.string().min(1).optional(),
  limitPrecedence: z.enum(["cad_mate", "manifest_override", "external_calibrated"]).default("cad_mate"),
  links: z.array(urdfLinkManifestSchema).min(1),
  joints: z.array(urdfJointSchema),
});

function normalizeManifestInput(input: unknown): unknown {
  if (typeof input !== "object" || input === null || Array.isArray(input)) {
    return input;
  }
  const source = input as Record<string, unknown>;
  const links = Array.isArray(source.links)
    ? source.links.map((link) => {
      if (typeof link !== "object" || link === null || Array.isArray(link)) {
        return link;
      }
      const entry = link as Record<string, unknown>;
      return {
        ...entry,
        linkName: entry.linkName ?? entry.name,
        frameRef: entry.frameRef ?? entry.frame_ref,
      };
    })
    : source.links;
  const joints = Array.isArray(source.joints)
    ? source.joints.map((joint) => {
      if (typeof joint !== "object" || joint === null || Array.isArray(joint)) {
        return joint;
      }
      const entry = joint as Record<string, unknown>;
      const override = typeof entry.limit_override === "object" && entry.limit_override !== null
        ? entry.limit_override as Record<string, unknown>
        : undefined;
      return {
        ...entry,
        jointName: entry.jointName ?? entry.name,
        axisRef: entry.axisRef ?? entry.axis_ref,
        axisBody: entry.axisBody ?? entry.axis_component,
        limitMate: entry.limitMate ?? entry.limit_mate,
        axisSign: entry.axisSign ?? entry.axis_sign,
        limitLower: entry.limitLower ?? override?.lower_rad,
        limitUpper: entry.limitUpper ?? override?.upper_rad,
        limitSign: entry.limitSign ?? entry.limit_sign,
      };
    })
    : source.joints;
  return {
    ...source,
    schemaVersion: source.schemaVersion ?? source.version ?? 1,
    robotName: source.robotName ?? source.robot_name ?? "cad_package",
    assemblyPath: source.assemblyPath ?? source.assembly_path,
    packageRoot: source.packageRoot ?? source.package_root,
    urdfOutputPath: source.urdfOutputPath ?? source.urdf_output_path,
    zeroConfiguration: source.zeroConfiguration
      ?? (typeof source.zero_configuration === "string"
        ? source.zero_configuration
        : "export_pose"),
    limitPrecedence: source.limitPrecedence ?? source.limit_precedence ?? "cad_mate",
    links,
    joints,
  };
}

export const urdfJointManifestFileSchema = z.preprocess(
  normalizeManifestInput,
  urdfJointManifestFileBaseSchema,
);

export const urdfJointManifestSchema = urdfJointManifestFileSchema;

export const cadUrdfPackageSchema = z.object({
  schemaVersion: z.literal(1),
  units: z.object({
    length: z.literal("m"),
    angle: z.literal("rad"),
    mass: z.literal("kg"),
  }),
  handedness: z.literal("right"),
  rotation: z.literal("rpy_intrinsic_xyz"),
  robotName: z.string().min(1),
  assemblyPath: z.string().min(1),
  exportedAtUtc: z.string().min(1),
  zeroConfiguration: z.string().optional(),
  limitPrecedence: z.enum(["cad_mate", "manifest_override", "external_calibrated"]),
  links: z.array(
    z.object({
      linkName: z.string().min(1),
      bodies: z.array(z.string().min(1)),
      frameRef: z.string().min(1),
      poseWorld: poseSchema,
      massKg: z.number().nonnegative(),
      comInLink: vec3Schema,
      inertiaAboutLink: inertiaSchema,
      visualMesh: z.string().min(1),
      collisionMesh: z.string().min(1),
      meshOrigin: poseSchema.optional(),
      warnings: z.array(z.string()).optional(),
    }),
  ),
  joints: z.array(
    z.object({
      jointName: z.string().min(1),
      type: z.enum(["revolute", "continuous", "fixed"]),
      parent: z.string().min(1),
      child: z.string().min(1),
      origin: poseSchema,
      axis: vec3Schema,
      limit: z
        .object({
          lower: z.number(),
          upper: z.number(),
          effort: z.number().optional(),
          velocity: z.number().optional(),
          source: z.enum(["cad_mate", "manifest_override", "external_calibrated", "none"]),
          mateName: z.string().optional(),
        })
        .optional(),
      warnings: z.array(z.string()).optional(),
    }),
  ),
  warnings: z.array(z.string()).optional(),
});

export const exportUrdfPackageSchema = z.object({
  path: z.string().min(1).optional(),
  assembly_path: z.string().min(1).optional(),
  manifest_path: z.string().min(1).optional(),
  manifest: urdfJointManifestFileSchema.optional(),
  confirm: z.literal(true).optional(),
}).refine(
  (data) => Boolean(data.manifest_path) !== Boolean(data.manifest),
  { message: "Provide exactly one of manifest_path or manifest." },
).refine(
  (data) => Boolean(data.path || data.assembly_path || data.manifest?.assemblyPath),
  { message: "Provide an assembly path via path, assembly_path, or the inline manifest." },
);

export const generateUrdfSchema = z.object({
  package_path: z.string().min(1),
  urdf_output_path: z.string().min(1).optional(),
  mesh_uri_prefix: z.string().optional(),
});

export const getMateLimitAngleSchema = z.object({
  path: z.string().min(1),
  mate_name: z.string().min(1),
});
