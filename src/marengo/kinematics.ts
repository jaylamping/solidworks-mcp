import fs from "node:fs/promises";

import { resolveMarengoFile } from "./root.js";

export interface KinematicsJoint {
  name: string;
  actuator: string;
  parentChild: string;
  axis: string;
  lowerRad: number | null;
  upperRad: number | null;
  effortNm: number | null;
  notes: string;
}

export async function loadKinematicsJoints(): Promise<KinematicsJoint[]> {
  const filePath = await resolveMarengoFile("hardware/docs/kinematics.md");
  const raw = await fs.readFile(filePath, "utf8");
  return parseKinematicsTable(raw);
}

export function parseKinematicsTable(markdown: string): KinematicsJoint[] {
  const joints: KinematicsJoint[] = [];
  for (const line of markdown.split("\n")) {
    const trimmed = line.trim();
    if (!trimmed.startsWith("|") || trimmed.includes("---") || trimmed.toLowerCase().includes("| joint |")) {
      continue;
    }

    const cols = trimmed
      .split("|")
      .map((cell) => cell.trim())
      .filter((cell) => cell.length > 0);
    if (cols.length < 6 || cols[0].toLowerCase() === "joint") {
      continue;
    }

    joints.push({
      name: cols[0].replace(/`/g, ""),
      actuator: cols[1],
      parentChild: cols[2],
      axis: cols[3],
      lowerRad: parseNumber(cols[4]),
      upperRad: parseNumber(cols[5]),
      effortNm: cols.length > 6 ? parseNumber(cols[6]) : null,
      notes: cols.length > 7 ? cols[7] : "",
    });
  }
  return joints;
}

function parseNumber(value: string): number | null {
  const parsed = Number.parseFloat(value);
  return Number.isFinite(parsed) ? parsed : null;
}
