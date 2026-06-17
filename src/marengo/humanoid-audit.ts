import { assertAllowedPath } from "../config.js";
import { runWorker } from "../worker.js";

export interface HumanoidAuditOptions {
  path?: string;
}

export async function linkMassProperties(options: HumanoidAuditOptions = {}) {
  const path = options.path ? assertAllowedPath(options.path) : undefined;
  const components = path
    ? ((await runWorker({ command: "list_components", args: { path } })) as { components?: Array<{ name?: string }> })
    : { components: [] };

  const links = new Map<string, { components: string[] }>();
  for (const row of components.components ?? []) {
    const name = row.name ?? "";
    const prefix = name.split("_")[0] ?? name;
    if (!links.has(prefix)) {
      links.set(prefix, { components: [] });
    }
    links.get(prefix)?.components.push(name);
  }

  const massByLink: Record<string, unknown> = {};
  if (path) {
    for (const [link, info] of links) {
      const sample = info.components[0];
      if (!sample) {
        continue;
      }
      massByLink[link] = await runWorker({
        command: "get_mass_properties",
        args: { path, component_name: sample },
      });
    }
  }

  return { path, linkCount: links.size, links: Object.fromEntries(links), massByLink };
}

export async function jointAxisExtract(options: HumanoidAuditOptions = {}) {
  const path = options.path ? assertAllowedPath(options.path) : undefined;
  const refs = path
    ? await runWorker({ command: "list_reference_geometry", args: { path } })
    : { items: [] };

  const axes = ((refs as { items?: Array<{ name?: string; type?: string }> }).items ?? []).filter(
    (item) =>
      (item.name ?? "").includes("joint")
      || (item.name ?? "").includes("urdf")
      || (item.type ?? "").toLowerCase().includes("axis"),
  );

  return { path, axisCount: axes.length, axes };
}

export async function bilateralSymmetryCheck(options: HumanoidAuditOptions = {}) {
  const path = options.path ? assertAllowedPath(options.path) : undefined;
  if (!path) {
    return { ok: false, reason: "path_required" };
  }

  const components = (await runWorker({ command: "list_components", args: { path } })) as {
    components?: Array<{ name?: string }>;
  };
  const names = (components.components ?? []).map((c) => c.name ?? "");
  const left = names.filter((n) => n.includes("left"));
  const right = names.filter((n) => n.includes("right"));

  const pairs: Array<{ left: string; right: string | null }> = [];
  for (const l of left) {
    const candidate = l.replace(/left/gi, "right");
    pairs.push({ left: l, right: names.includes(candidate) ? candidate : null });
  }

  return {
    path,
    leftCount: left.length,
    rightCount: right.length,
    pairedCount: pairs.filter((p) => p.right).length,
    unpairedLeft: pairs.filter((p) => !p.right).map((p) => p.left),
  };
}

export async function actuatorEnvelopeCheck(options: HumanoidAuditOptions = {}) {
  const path = options.path ? assertAllowedPath(options.path) : undefined;
  if (!path) {
    return { ok: false, reason: "path_required" };
  }

  const interferences = await runWorker({ command: "list_interferences", args: { path } });
  const motors = ((await runWorker({ command: "list_components", args: { path } })) as {
    components?: Array<{ name?: string }>;
  }).components?.filter((c) => (c.name ?? "").includes("actuator"));

  return { path, motors, interferences };
}

export async function configVariantDiff(options: HumanoidAuditOptions = {}) {
  const path = options.path ? assertAllowedPath(options.path) : undefined;
  const dimensions = path
    ? await runWorker({ command: "list_dimensions", args: { path } })
    : { dimensions: [] };
  const configurations = path
    ? await runWorker({ command: "list_configurations", args: { path } })
    : { configurations: [] };

  return { path, dimensions, configurations };
}

export async function referenceGeometryAudit(options: HumanoidAuditOptions = {}) {
  const path = options.path ? assertAllowedPath(options.path) : undefined;
  if (!path) {
    return { ok: false, reason: "path_required" };
  }

  const refs = (await runWorker({ command: "list_reference_geometry", args: { path } })) as {
    items?: Array<{ name?: string; type?: string }>;
  };
  const urdfRefs = (refs.items ?? []).filter((item) => (item.name ?? "").toLowerCase().includes("urdf"));
  const missingUrdfPrefix = (refs.items ?? []).filter(
    (item) => !(item.name ?? "").toLowerCase().startsWith("urdf_"),
  );

  return {
    path,
    totalRefs: refs.items?.length ?? 0,
    urdfRefCount: urdfRefs.length,
    urdfRefs,
    nonUrdfNamedRefs: missingUrdfPrefix.slice(0, 20),
  };
}

export async function jointLimitValidate(options: HumanoidAuditOptions = {}) {
  const path = options.path ? assertAllowedPath(options.path) : undefined;
  if (!path) {
    return { ok: false, reason: "path_required" };
  }

  const mates = (await runWorker({ command: "list_mates", args: { path } })) as {
    mates?: Array<{ name?: string; type?: string }>;
  };
  const limitMates = (mates.mates ?? []).filter(
    (m) => (m.type ?? "").toLowerCase().includes("limit") || (m.type ?? "").toLowerCase().includes("angle"),
  );

  return {
    path,
    limitMateCount: limitMates.length,
    limitMates,
    kinematicsNote: "Compare limits to hardware/docs/kinematics.md joint bounds manually or via marengo_kinematics_consistency.",
  };
}

export async function subassemblySyncCheck(options: HumanoidAuditOptions = {}) {
  const path = options.path ? assertAllowedPath(options.path) : undefined;
  if (!path) {
    return { ok: false, reason: "path_required" };
  }

  const components = (await runWorker({ command: "list_components", args: { path } })) as {
    components?: Array<{ name?: string; path?: string }>;
  };
  const frame = (components.components ?? []).filter((c) =>
    (c.name ?? "").includes("torso_frame") || (c.path ?? "").includes("torso_frame"),
  );
  const broken = await runWorker({ command: "list_broken_references", args: { path } });

  return { path, frameComponents: frame, broken };
}

export async function exportBomCsv(options: HumanoidAuditOptions = {}) {
  const path = options.path ? assertAllowedPath(options.path) : undefined;
  if (!path) {
    return { ok: false, reason: "path_required" };
  }

  const bom = (await runWorker({ command: "list_bom", args: { path } })) as {
    lines?: Array<{ name?: string; path?: string; quantity?: number }>;
  };
  const lines = bom.lines ?? [];
  const header = "name,path,quantity";
  const rows = lines.map((row) =>
    [row.name ?? "", row.path ?? "", String(row.quantity ?? 1)]
      .map((cell) => `"${cell.replace(/"/g, '""')}"`)
      .join(","),
  );

  return {
    path,
    lineCount: lines.length,
    csv: [header, ...rows].join("\n"),
    note: "Import into hardware/bom/master-bom.csv after review.",
  };
}

export async function urdfExportPrepare(options: HumanoidAuditOptions = {}) {
  const path = options.path ? assertAllowedPath(options.path) : undefined;
  const readiness = path ? await runWorker({ command: "list_reference_geometry", args: { path } }) : null;
  const interferences = path ? await runWorker({ command: "list_interferences", args: { path } }) : null;

  return {
    path,
    readiness,
    interferences,
    checklist: [
      "Run marengo_urdf_readiness",
      "Run marengo_kinematics_consistency",
      "Export manually via Brawner to assets/urdf/marengo.urdf",
      "Run marengo_urdf_export_postcheck",
    ],
  };
}
