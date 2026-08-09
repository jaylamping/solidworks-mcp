import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const fixturePath = path.join(root, ".demo/urdf/brick-fixture.json");
const mathUrl = pathToFileURL(path.join(root, "src/urdf/math.ts")).href;

const {
  brickInertiaAtCenter,
  inertiaAboutFrame,
  childInParent,
  transformAxis,
  invertPose,
  rpyToMatrix,
  nearlyEqual,
} = await import(mathUrl);

const fixture = JSON.parse(fs.readFileSync(fixturePath, "utf8"));
const mass = fixture.mass_kg;
const size = fixture.size_m;
const Icom = brickInertiaAtCenter(mass, size);
const link = fixture.link_frame_in_source;
const R = rpyToMatrix(link.rpy);
const sourceToLink = invertPose(link);
const comInLink = [
  sourceToLink.xyz[0] + fixture.com_in_source[0],
  sourceToLink.xyz[1] + fixture.com_in_source[1],
  sourceToLink.xyz[2] + fixture.com_in_source[2],
];

const Ilink = inertiaAboutFrame(Icom, R, comInLink, mass);
const expectedI = fixture.expected.inertia_about_link;
const expectedCom = fixture.expected.com_in_link;

for (let i = 0; i < 3; i++) {
  assert.ok(
    nearlyEqual(comInLink[i], expectedCom[i], 1e-9),
    `com[${i}] ${comInLink[i]} != ${expectedCom[i]}`,
  );
}

for (const key of Object.keys(expectedI)) {
  assert.ok(
    nearlyEqual(Ilink[key], expectedI[key], 1e-9),
    `inertia.${key} ${Ilink[key]} != ${expectedI[key]}`,
  );
}

const joint = fixture.joint;
const origin = childInParent(joint.parent_world, joint.child_world);
for (let i = 0; i < 3; i++) {
  assert.ok(nearlyEqual(origin.xyz[i], joint.expected_origin.xyz[i], 1e-9));
  assert.ok(nearlyEqual(origin.rpy[i], joint.expected_origin.rpy[i], 1e-9));
}
const axis = transformAxis(joint.parent_world, joint.axis_world);
for (let i = 0; i < 3; i++) {
  assert.ok(nearlyEqual(axis[i], joint.expected_axis_in_parent[i], 1e-9));
}

console.log("urdf-math-fixture: ok");
