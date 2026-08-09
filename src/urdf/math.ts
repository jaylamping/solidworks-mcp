/** CAD→URDF numeric helpers. Units: meters, radians. Right-handed. */

export type Vec3 = readonly [number, number, number];

/** 3×3 row-major rotation matrix. */
export type Mat3 = readonly [
  number, number, number,
  number, number, number,
  number, number, number,
];

export type Pose = {
  xyz: Vec3;
  rpy: Vec3;
};

export type Inertia = {
  ixx: number;
  ixy: number;
  ixz: number;
  iyy: number;
  iyz: number;
  izz: number;
};

const EPS = 1e-12;

export function nearlyEqual(a: number, b: number, eps = 1e-9): boolean {
  return Math.abs(a - b) <= eps;
}

export function vecAdd(a: Vec3, b: Vec3): Vec3 {
  return [a[0] + b[0], a[1] + b[1], a[2] + b[2]];
}

export function vecSub(a: Vec3, b: Vec3): Vec3 {
  return [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
}

export function vecScale(a: Vec3, s: number): Vec3 {
  return [a[0] * s, a[1] * s, a[2] * s];
}

export function vecDot(a: Vec3, b: Vec3): number {
  return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
}

export function vecCross(a: Vec3, b: Vec3): Vec3 {
  return [
    a[1] * b[2] - a[2] * b[1],
    a[2] * b[0] - a[0] * b[2],
    a[0] * b[1] - a[1] * b[0],
  ];
}

export function vecNorm(a: Vec3): number {
  return Math.hypot(a[0], a[1], a[2]);
}

export function vecNormalize(a: Vec3): Vec3 {
  const n = vecNorm(a);
  if (n < EPS) {
    throw new Error("Cannot normalize near-zero vector");
  }
  return vecScale(a, 1 / n);
}

export function matMulVec(m: Mat3, v: Vec3): Vec3 {
  return [
    m[0] * v[0] + m[1] * v[1] + m[2] * v[2],
    m[3] * v[0] + m[4] * v[1] + m[5] * v[2],
    m[6] * v[0] + m[7] * v[1] + m[8] * v[2],
  ];
}

export function matMul(a: Mat3, b: Mat3): Mat3 {
  const row = (r: number, c: number) =>
    a[r * 3 + 0] * b[0 * 3 + c] + a[r * 3 + 1] * b[1 * 3 + c] + a[r * 3 + 2] * b[2 * 3 + c];
  return [
    row(0, 0), row(0, 1), row(0, 2),
    row(1, 0), row(1, 1), row(1, 2),
    row(2, 0), row(2, 1), row(2, 2),
  ];
}

export function matTranspose(m: Mat3): Mat3 {
  return [
    m[0], m[3], m[6],
    m[1], m[4], m[7],
    m[2], m[5], m[8],
  ];
}

/** Intrinsic XYZ (URDF rpy) → rotation matrix. */
export function rpyToMatrix(rpy: Vec3): Mat3 {
  const [r, p, y] = rpy;
  const cr = Math.cos(r);
  const sr = Math.sin(r);
  const cp = Math.cos(p);
  const sp = Math.sin(p);
  const cy = Math.cos(y);
  const sy = Math.sin(y);
  return [
    cy * cp, cy * sp * sr - sy * cr, cy * sp * cr + sy * sr,
    sy * cp, sy * sp * sr + cy * cr, sy * sp * cr - cy * sr,
    -sp, cp * sr, cp * cr,
  ];
}

export function matrixToRpy(m: Mat3): Vec3 {
  const sp = -m[6];
  const pitch = Math.abs(sp) >= 1 - 1e-10
    ? (Math.PI / 2) * Math.sign(sp)
    : Math.asin(sp);
  const roll = Math.atan2(m[7], m[8]);
  const yaw = Math.atan2(m[3], m[0]);
  return [roll, pitch, yaw];
}

export function poseToMatrix(pose: Pose): { R: Mat3; t: Vec3 } {
  return { R: rpyToMatrix(pose.rpy), t: pose.xyz };
}

/** Compose A then B: p_world = A * (B * p_local). */
export function composePose(a: Pose, b: Pose): Pose {
  const Ra = rpyToMatrix(a.rpy);
  const Rb = rpyToMatrix(b.rpy);
  const R = matMul(Ra, Rb);
  const t = vecAdd(a.xyz, matMulVec(Ra, b.xyz));
  return { xyz: t, rpy: matrixToRpy(R) };
}

/** Inverse pose. */
export function invertPose(pose: Pose): Pose {
  const R = rpyToMatrix(pose.rpy);
  const Rt = matTranspose(R);
  const t = vecScale(matMulVec(Rt, pose.xyz), -1);
  return { xyz: t, rpy: matrixToRpy(Rt) };
}

/**
 * URDF joint origin: child pose expressed in parent frame.
 * Given parent and child poses in the same world/assembly frame.
 */
export function childInParent(parentWorld: Pose, childWorld: Pose): Pose {
  return composePose(invertPose(parentWorld), childWorld);
}

/** Rotate an axis direction into another frame (unit output). */
export function transformAxis(parentWorld: Pose, axisWorld: Vec3): Vec3 {
  const Rinv = matTranspose(rpyToMatrix(parentWorld.rpy));
  return vecNormalize(matMulVec(Rinv, axisWorld));
}

/**
 * Parallel-axis + rotation for inertia tensors.
 * `Icom` is about COM in source axes; `R` rotates source → target; `comInTarget` is COM in target frame; `mass` in kg.
 */
export function inertiaAboutFrame(
  Icom: Inertia,
  R: Mat3,
  comInTarget: Vec3,
  mass: number,
): Inertia {
  const Is: Mat3 = [
    Icom.ixx, Icom.ixy, Icom.ixz,
    Icom.ixy, Icom.iyy, Icom.iyz,
    Icom.ixz, Icom.iyz, Icom.izz,
  ];
  const Rt = matTranspose(R);
  const rotated = matMul(matMul(R, Is), Rt);
  const [x, y, z] = comInTarget;
  const xx = x * x;
  const yy = y * y;
  const zz = z * z;
  return {
    ixx: rotated[0] + mass * (yy + zz),
    ixy: rotated[1] - mass * x * y,
    ixz: rotated[2] - mass * x * z,
    iyy: rotated[4] + mass * (xx + zz),
    iyz: rotated[5] - mass * y * z,
    izz: rotated[8] + mass * (xx + yy),
  };
}

/** Solid brick inertia about geometric center, axes aligned with edges. */
export function brickInertiaAtCenter(mass: number, size: Vec3): Inertia {
  const [a, b, c] = size;
  const aa = a * a;
  const bb = b * b;
  const cc = c * c;
  return {
    ixx: (mass / 12) * (bb + cc),
    ixy: 0,
    ixz: 0,
    iyy: (mass / 12) * (aa + cc),
    iyz: 0,
    izz: (mass / 12) * (aa + bb),
  };
}

export function aggregateMassCom(
  parts: Array<{ mass: number; com: Vec3 }>,
): { mass: number; com: Vec3 } {
  let mass = 0;
  let mx = 0;
  let my = 0;
  let mz = 0;
  for (const part of parts) {
    mass += part.mass;
    mx += part.mass * part.com[0];
    my += part.mass * part.com[1];
    mz += part.mass * part.com[2];
  }
  if (mass < EPS) {
    throw new Error("Aggregate mass is zero");
  }
  return { mass, com: [mx / mass, my / mass, mz / mass] };
}
