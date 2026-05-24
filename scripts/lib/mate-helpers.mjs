import { runWorker } from "./worker-client.mjs";

const ALIGNED = "aligned";
const ANTI_ALIGNED = "anti_aligned";

/**
 * @param {Record<string, unknown>} args
 */
export function tryMateCoincident(args) {
  return runWorker("mate_try_coincident", { align: ALIGNED, rebuild: false, ...args });
}

/**
 * @param {Record<string, unknown>} args
 */
export function tryMateParallel(args) {
  return runWorker("mate_try_parallel", { align: ALIGNED, rebuild: false, ...args });
}

/**
 * @param {Record<string, unknown> & { path: string }} baseArgs
 * @param {Array<Record<string, unknown>>} attempts
 */
export function mateFirstThatWorks(baseArgs, attempts) {
  for (const attempt of attempts) {
    const result = runWorker("mate_try_coincident", {
      rebuild: false,
      ...baseArgs,
      ...attempt,
    });
    if (result?.ok || result?.mateResult?.mateCreated || result?.mateResult?.alreadyConstrained) {
      return { ok: true, attempt, result };
    }
  }
  return { ok: false, attempts: attempts.length };
}

/**
 * Height mate with face then plane fallback (compute shelf pattern).
 * @param {object} opts
 * @param {string} opts.path
 * @param {string} opts.shelfComponent
 * @param {string} opts.layoutComponent
 * @param {number} opts.shelfFaceIndex
 */
export function mateShelfHeightWithFallback(opts) {
  const { path, shelfComponent, layoutComponent, shelfFaceIndex } = opts;
  const mates = [];

  for (const align of [ALIGNED, ANTI_ALIGNED]) {
    const result = runWorker("mate_try_coincident", {
      path,
      component_1: shelfComponent,
      ref_1: "Boss-Extrude1",
      face_index_1: shelfFaceIndex,
      component_2: layoutComponent,
      ref_2: "compute_shelf_seat",
      align,
      rebuild: false,
    });
    mates.push({ kind: `face_height_${align}`, result });
    if (result?.ok || result?.mateResult?.mateCreated) {
      return { heightOk: true, mates };
    }
  }

  for (const align of [ALIGNED, ANTI_ALIGNED]) {
    for (const shelfRef of ["mount_face", "Top Plane"]) {
      const result = runWorker("mate_try_coincident", {
        path,
        component_1: shelfComponent,
        ref_1: shelfRef,
        component_2: layoutComponent,
        ref_2: "compute_shelf_seat",
        align,
        rebuild: false,
      });
      mates.push({ kind: `height_${shelfRef}_${align}`, result });
      if (result?.ok || result?.mateResult?.mateCreated) {
        return { heightOk: true, mates };
      }
    }
  }

  return { heightOk: false, mates };
}

export { ALIGNED, ANTI_ALIGNED };
