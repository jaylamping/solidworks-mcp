# solidworks_create_hinge_limit

Build a strict coaxial+mount+axis-backed LimitAngle hinge on an explicit align/flip branch. Suppresses only release_mates, refuses NoAxis/standard-angle fallbacks, force-rebuilds, probes motion/clearance samples, and restores the pre-change checkpoint on any failed stage (ok:false + restored). Does not save — after a collision-free preview looks good, call confirm_and_save and require ok/poseStable.

| Field | Value |
|-------|-------|
| Worker command | `create_hinge_limit` |
| Tier | core |
| Read only | false |
| Destructive | false |
| Confirm required | false |
| Description source | authored |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | yes | - | Assembly path under an allowed CAD root. (allowed root) |
| `fixed_component` | string | yes | - | Stationary / grounded component name. |
| `moving_component` | string | yes | - | Component that rotates about the hinge. |
| `fixed_axis_ref` | string | yes | - | Axis (or coaxial cylindrical ref) on the fixed component. |
| `moving_axis_ref` | string | yes | - | Axis (or coaxial cylindrical ref) on the moving component. |
| `fixed_mount_plane_ref` | string | yes | - | Mounting plane / face on the fixed component. |
| `moving_mount_plane_ref` | string | yes | - | Mounting plane / face on the moving component. |
| `fixed_angle_plane_ref` | string | yes | - | Angle-measurement plane on the fixed component. |
| `moving_angle_plane_ref` | string | yes | - | Angle-measurement plane on the moving component. |
| `min_angle_deg` | number | yes | - | Minimum LimitAngle bound in degrees. |
| `max_angle_deg` | number | yes | - | Maximum LimitAngle bound in degrees. |
| `park_angle_deg` | number | yes | - | Initial / parked LimitAngle value in degrees. |
| `align` | `aligned`, `anti_aligned` | yes | - | Explicit LimitAngle mate alignment branch. |
| `flip_dimension` | boolean | yes | - | Explicit LimitAngle FlipDimension branch. |
| `release_mates` | string[] | yes | - | Legacy grounding mates to suppress before building the hinge (retained for rollback via checkpoint). |
| `axis_align` | `aligned`, `anti_aligned` | no | - | Optional coaxial axis-mate alignment. Defaults to aligned. |
| `mount_align` | `aligned`, `anti_aligned` | no | - | Optional mounting-plane mate alignment. Defaults to aligned. |
| `probe_angles_deg` | number[] | no | - | Optional angles to drive after construction (defaults to park, min, mid, max). |
| `clearance_component` | string | no | - | Optional component whose bounding-box center must stay on the clear side. |
| `clearance_axis` | `x`, `y`, `z` | no | - | Assembly axis used for clearance_component center checks. Defaults to y. |
| `clearance_max` | number | no | - | Fail if clearance_component center on clearance_axis is greater than this (meters). |
| `clearance_min` | number | no | - | Fail if clearance_component center on clearance_axis is less than this (meters). |

## Tags

- mate
- hinge
- limit

## Domains

- assembly
- mate

