import fs from "node:fs/promises";

import { runWorker } from "../worker.js";
import { loadCadConventions } from "./manifests.js";
import { loadKinematicsJoints } from "./kinematics.js";
import { resolveMarengoFile } from "./root.js";
import type { AuditFinding, AuditLevel } from "./cad-audit.js";

function finding(level: AuditLevel, code: string, message: string, filePath?: string): AuditFinding {
  return { level, code, message, ...(filePath ? { path: filePath } : {}) };
}

function summarize(findings: AuditFinding[]) {
  const fail = findings.filter((f) => f.level === "fail").length;
  const warn = findings.filter((f) => f.level === "warn").length;
  return { pass: findings.filter((f) => f.level === "pass").length, warn, fail, ok: fail === 0 };
}

export async function urdfReadiness(args: { path?: string }): Promise<unknown> {
  const conventions = await loadCadConventions();
  const kinematics = await loadKinematicsJoints();
  const findings: AuditFinding[] = [];

  const refData = (await runWorker({
    command: "list_reference_geometry",
    args: { path: args.path },
  })) as { referenceGeometry?: Array<{ name?: string | null }> };

  const refNames = new Set(
    (refData.referenceGeometry ?? [])
      .map((item) => (item.name ?? "").toLowerCase())
      .filter(Boolean),
  );

  for (const required of conventions.requiredUrdfReferenceNames) {
    if (!refNames.has(required.toLowerCase())) {
      findings.push(
        finding("fail", "missing_reference", `Missing named reference geometry: ${required}`, args.path),
      );
    } else {
      findings.push(finding("pass", "reference_present", `Found reference: ${required}`, args.path));
    }
  }

  for (const recommended of conventions.recommendedReferenceNames) {
    if (!refNames.has(recommended.toLowerCase())) {
      findings.push(finding("warn", "recommended_reference", `Recommended reference missing: ${recommended}`, args.path));
    }
  }

  if (kinematics.length === 0) {
    findings.push(finding("warn", "kinematics_empty", "No joints parsed from hardware/docs/kinematics.md"));
  }

  return {
    kinematicsJointCount: kinematics.length,
    referenceNames: [...refNames],
    findings,
    summary: summarize(findings),
  };
}

export async function kinematicsConsistency(args: { path?: string }): Promise<unknown> {
  const kinematics = await loadKinematicsJoints();
  const findings: AuditFinding[] = [];

  const components = (await runWorker({
    command: "list_components",
    args: { path: args.path },
  })) as { components?: Array<{ name?: string | null; path?: string | null }> };

  const instanceNames = new Set(
    (components.components ?? [])
      .map((row) => (row.name ?? "").toLowerCase())
      .filter(Boolean),
  );

  for (const joint of kinematics) {
    const jointToken = joint.name.toLowerCase();
    const matched = [...instanceNames].some(
      (name) => name.includes(jointToken) || jointToken.includes(name.replace(/-\d+$/, "")),
    );
    if (!matched) {
      findings.push(
        finding(
          "warn",
          "joint_instance_missing",
          `No assembly component name matches kinematics joint: ${joint.name}`,
          args.path,
        ),
      );
    } else {
      findings.push(finding("pass", "joint_instance", `Kinematics joint reflected in assembly tree: ${joint.name}`));
    }
  }

  return {
    kinematicsJoints: kinematics.map((j) => j.name),
    componentCount: instanceNames.size,
    findings,
    summary: summarize(findings),
  };
}

export interface UrdfJointLimit {
  name: string;
  lower: number | null;
  upper: number | null;
  effort: number | null;
  velocity: number | null;
}

export async function urdfExportPostcheck(): Promise<unknown> {
  const urdfPath = await resolveMarengoFile("assets/urdf/marengo.urdf");
  const motorsPath = await resolveMarengoFile("config/motors.yaml");
  const rawUrdf = await fs.readFile(urdfPath, "utf8");
  const rawMotors = await fs.readFile(motorsPath, "utf8");
  const kinematics = await loadKinematicsJoints();
  const findings: AuditFinding[] = [];

  const urdfJoints = parseUrdfJoints(rawUrdf);
  const kinematicsNames = new Set(kinematics.map((j) => j.name));
  const motorLimits = parseMotorBenchLimits(rawMotors);

  for (const joint of urdfJoints) {
    if (kinematics.length > 0 && !kinematicsNames.has(joint.name)) {
      findings.push(
        finding("warn", "urdf_joint_not_in_kinematics", `URDF joint not listed in kinematics.md: ${joint.name}`, urdfPath),
      );
    }
  }

  for (const kinJoint of kinematics) {
    const urdfJoint = urdfJoints.find((j) => j.name === kinJoint.name);
    if (!urdfJoint) {
      findings.push(
        finding("fail", "kinematics_joint_missing_in_urdf", `Kinematics joint missing from URDF: ${kinJoint.name}`, urdfPath),
      );
      continue;
    }

    const motor = motorLimits.get(kinJoint.name);
    if (motor && kinJoint.lowerRad !== null && urdfJoint.lower !== null) {
      if (Math.abs(urdfJoint.lower - kinJoint.lowerRad) > 0.02) {
        findings.push(
          finding(
            "warn",
            "limit_lower_mismatch",
            `URDF lower limit for ${kinJoint.name} (${urdfJoint.lower}) differs from kinematics (${kinJoint.lowerRad})`,
            urdfPath,
          ),
        );
      }
    }
    if (motor && kinJoint.upperRad !== null && urdfJoint.upper !== null) {
      if (Math.abs(urdfJoint.upper - kinJoint.upperRad) > 0.02) {
        findings.push(
          finding(
            "warn",
            "limit_upper_mismatch",
            `URDF upper limit for ${kinJoint.name} (${urdfJoint.upper}) differs from kinematics (${kinJoint.upperRad})`,
            urdfPath,
          ),
        );
      }
    }
    if (motor && urdfJoint.lower !== null && motor.lower !== null && Math.abs(urdfJoint.lower - motor.lower) > 0.02) {
      findings.push(
        finding(
          "warn",
          "limit_lower_vs_motors",
          `URDF lower for ${kinJoint.name} differs from config/motors.yaml bench limit`,
          urdfPath,
        ),
      );
    }
    if (motor && urdfJoint.upper !== null && motor.upper !== null && Math.abs(urdfJoint.upper - motor.upper) > 0.02) {
      findings.push(
        finding(
          "warn",
          "limit_upper_vs_motors",
          `URDF upper for ${kinJoint.name} differs from config/motors.yaml bench limit`,
          urdfPath,
        ),
      );
    }
  }

  if (urdfJoints.length === 0) {
    findings.push(finding("fail", "urdf_no_joints", "No revolute/prismatic joints found in URDF", urdfPath));
  }

  return {
    urdfPath,
    urdfJointNames: urdfJoints.map((j) => j.name),
    kinematicsJointNames: kinematics.map((j) => j.name),
    findings,
    summary: summarize(findings),
  };
}

export function parseUrdfJoints(xml: string): UrdfJointLimit[] {
  const joints: UrdfJointLimit[] = [];
  const jointBlocks = xml.match(/<joint[\s\S]*?<\/joint>/gi) ?? [];
  for (const block of jointBlocks) {
    const name = block.match(/name="([^"]+)"/i)?.[1];
    const type = block.match(/type="([^"]+)"/i)?.[1];
    if (!name || type === "fixed") {
      continue;
    }
    const limitTag = block.match(/<limit[^>]*>/i)?.[0] ?? "";
    joints.push({
      name,
      lower: parseAttr(limitTag, "lower"),
      upper: parseAttr(limitTag, "upper"),
      effort: parseAttr(limitTag, "effort"),
      velocity: parseAttr(limitTag, "velocity"),
    });
  }
  return joints;
}

function parseAttr(tag: string, name: string): number | null {
  const match = tag.match(new RegExp(`${name}="([^"]+)"`, "i"));
  if (!match) {
    return null;
  }
  const parsed = Number.parseFloat(match[1]);
  return Number.isFinite(parsed) ? parsed : null;
}

function parseMotorBenchLimits(yaml: string): Map<string, { lower: number | null; upper: number | null }> {
  const limits = new Map<string, { lower: number | null; upper: number | null }>();
  const blocks = yaml.split(/\n\s*-\s+joint:/);
  for (const block of blocks.slice(1)) {
    const joint = block.match(/joint:\s*(\S+)/)?.[1];
    if (!joint) {
      continue;
    }
    const lower = parseYamlScalar(block, "position_lower_rad");
    const upper = parseYamlScalar(block, "position_upper_rad");
    limits.set(joint, { lower, upper });
  }
  return limits;
}

function parseYamlScalar(block: string, key: string): number | null {
  const match = block.match(new RegExp(`${key}:\\s*(-?[0-9.]+)`));
  if (!match) {
    return null;
  }
  const parsed = Number.parseFloat(match[1]);
  return Number.isFinite(parsed) ? parsed : null;
}
