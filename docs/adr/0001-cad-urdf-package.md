# ADR 0001: CAD → package → URDF

## Status

Accepted

## Context

SolidWorks MCP must prepare assemblies for detailed URDF without a manual add-in. Product joint trees stay in caller manifests. Extraction lives in the COM worker; URDF XML generation is Node-only.

## Decision

Ship a versioned **CadUrdfPackage** produced by `export_urdf_package` from a caller **UrdfJointManifest**, then `generate_urdf` in Node.

### Numeric contract

| Quantity | Convention |
|----------|------------|
| Length | meters |
| Angle | radians in package; mate tools may expose degrees at the MCP edge |
| Frames | right-handed |
| Rotation | `rpy` = intrinsic XYZ (URDF) |
| Joint origin | child link frame pose expressed in parent link frame |
| Joint axis | unit vector in the **parent** frame |
| Inertia | `ixx…izz` about the link `frame_ref`, not SolidWorks default COM axes |
| Mesh vertices | expressed in `frame_ref`; if export cannot bind CSYS, package stores `meshOrigin` (mesh→link) so the writer can correct |
| Export pose | assembly configuration at extract time is URDF `q = 0` |
| Limits | from `limit_mate` when `limit_precedence` is `cad_mate`; never silently overwrite `external_calibrated` |
| Collision (v1) | byte copy of the visual STL |
| Paths | `PathGuard` / allowed roots; package written via temp dir + atomic replace; mesh paths POSIX-relative to package root |
| Idempotency | semantic equality within ε on poses/masses/limits; ignore timestamps and STL binary noise |

### Manifest

Links list `bodies[]` (instance `Name2` strings). Joints name parent/child links, `axis_ref`, optional `limit_mate`, and effort/velocity defaults.

### Out of scope

Hardcoded product joint trees in the worker or consumer-specific CAD workflows.

## Consequences

- Golden fixture under `.demo/urdf/` must pass before extract merges.
- Consumer repos own manifests and asset promotion.
