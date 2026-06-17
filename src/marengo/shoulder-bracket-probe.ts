import { formatErrorForMcp } from "../errors.js";
import { assertAllowedPath } from "../config.js";
import { runWorker, type WorkerCommand } from "../worker.js";
import { resolveMarengoFile } from "./root.js";

export type ShoulderSide = "left" | "right";

export interface ShoulderBracketProbeOptions {
  path?: string;
  side?: ShoulderSide;
  model?: string;
}

const DEFAULT_MODEL = "rs03";
const LAYOUT_REL = "hardware/cad/parts/marengo_torso_layout.SLDPRT";
const ASM_REL = "hardware/cad/assemblies/marengo_torso_asm.SLDASM";

function motorComponentName(side: ShoulderSide, model: string): string {
  return `actuator_${model}_${side}_shoulder_pitch`;
}

function bracketPartName(side: ShoulderSide): string {
  return `marengo_shoulder_pitch_mount_bracket_${side}`;
}

async function workerOrError(command: WorkerCommand, args: Record<string, unknown>) {
  try {
    return { ok: true as const, data: await runWorker({ command, args }) };
  } catch (error) {
    return { ok: false as const, error: formatErrorForMcp(error) };
  }
}

export async function shoulderBracketProbe(options: ShoulderBracketProbeOptions = {}) {
  const side = options.side ?? "right";
  const model = (options.model ?? DEFAULT_MODEL).toLowerCase();
  const asmPath = options.path
    ? assertAllowedPath(options.path)
    : await resolveMarengoFile(ASM_REL);
  const layoutPath = await resolveMarengoFile(LAYOUT_REL);

  const motorName = motorComponentName(side, model);
  const oppositeMotorName = motorComponentName(side === "left" ? "right" : "left", model);

  const [
    envelopeResult,
    layoutRefsResult,
    motorBoxResult,
    oppositeMotorBoxResult,
    motorTransformResult,
    layoutTransformResult,
    componentsResult,
  ] = await Promise.all([
    workerOrError("actuator_get_envelope", { model }),
    workerOrError("list_reference_geometry", { path: layoutPath }),
    workerOrError("get_component_box", { path: asmPath, component_name: motorName }),
    workerOrError("get_component_box", { path: asmPath, component_name: oppositeMotorName }),
    workerOrError("get_component_transform", { path: asmPath, component_name: motorName }),
    workerOrError("get_component_transform", { path: asmPath, component_name: "marengo_torso_layout" }),
    workerOrError("list_components", { path: asmPath }),
  ]);

  const layoutRefs = layoutRefsResult.ok ? layoutRefsResult.data : null;
  const refItems = ((layoutRefs as { items?: Array<{ name?: string; type?: string }> } | null)?.items ?? [])
    .filter((item) => {
      const name = (item.name ?? "").toLowerCase();
      return name.includes("shoulder_mount") || name.includes("shoulder_plane");
    });

  const env = (envelopeResult.ok ? envelopeResult.data : {}) as {
    model?: string;
    envelopeWidthMm?: number;
    envelopeDepthMm?: number;
    envelopeHeightMm?: number;
    defaultClearanceMm?: number;
    vendorPath?: string;
  };

  const clearanceMm = env.defaultClearanceMm ?? 0.5;
  const depthTargetMm = Math.min(60, Math.round((env.envelopeDepthMm ?? 57) + clearanceMm));
  const spanYMm = 50;
  const wallMm = { min: 4, max: 6, recommended: 5 };

  const components = componentsResult.ok ? componentsResult.data : null;
  const componentNames = ((components as { components?: Array<{ name?: string }> } | null)?.components ?? [])
    .map((c) => c.name ?? "");

  const partialErrors = [
    !envelopeResult.ok ? { step: "actuator_get_envelope", error: envelopeResult.error } : null,
    !layoutRefsResult.ok ? { step: "list_reference_geometry", error: layoutRefsResult.error } : null,
    !motorBoxResult.ok ? { step: "get_component_box", target: motorName, error: motorBoxResult.error } : null,
    !oppositeMotorBoxResult.ok
      ? { step: "get_component_box", target: oppositeMotorName, error: oppositeMotorBoxResult.error }
      : null,
    !motorTransformResult.ok
      ? { step: "get_component_transform", target: motorName, error: motorTransformResult.error }
      : null,
    !layoutTransformResult.ok
      ? { step: "get_component_transform", target: "marengo_torso_layout", error: layoutTransformResult.error }
      : null,
    !componentsResult.ok ? { step: "list_components", error: componentsResult.error } : null,
  ].filter(Boolean);

  return {
    path: asmPath,
    side,
    model,
    layoutPath,
    complete: partialErrors.length === 0,
    partialErrors,
    envelope: env,
    shoulderReferences: refItems,
    motorInstances: {
      target: {
        name: motorName,
        box: motorBoxResult.ok ? motorBoxResult.data : null,
        transform: motorTransformResult.ok ? motorTransformResult.data : null,
        error: motorBoxResult.ok && motorTransformResult.ok ? undefined : partialErrors,
      },
      opposite: {
        name: oppositeMotorName,
        box: oppositeMotorBoxResult.ok ? oppositeMotorBoxResult.data : null,
        error: oppositeMotorBoxResult.ok ? undefined : oppositeMotorBoxResult.error,
      },
    },
    layoutInAssembly: layoutTransformResult.ok ? layoutTransformResult.data : null,
    bracketPart: {
      file: `hardware/cad/parts/${bracketPartName(side)}.SLDPRT`,
      componentName: bracketPartName(side),
      presentInAssembly: componentNames.some((n) => n === bracketPartName(side) || n.startsWith(`${bracketPartName(side)}-`)),
    },
    suggestedStarterBlock: {
      depthMm: depthTargetMm,
      depthMaxMm: 60,
      spanYMm,
      spanNote: "Half of RS03 Y envelope per side; center in each cage half",
      wallMm,
      clearanceMm,
      orientationNote:
        "Pitch axis +Y; pack so envelope width spans Y (~99 mm) and depth spans X (~57 mm) inside 85×110 mm cage.",
    },
    iterativeWorkflow: [
      "marengo_shoulder_bracket_probe — starting dims (this tool)",
      "Checkpoint assembly, sketch mount_face on inner rail, extrude boss to suggestedStarterBlock",
      "Highlight bracket in SW → marengo_resolve_selection (confirm referent)",
      "marengo_actuator_cut_cavity with use_selection: true (or any tool + use_selection for 'this' entity)",
      "list_interferences / list_dimensions / set_dimension — tweak and repeat",
    ],
    docRef: "hardware/docs/torso-actuator-brackets.md",
  };
}
