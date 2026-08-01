# solidworks_set_mate_limit_angle

Update min/max/current angle on an existing limit-angle mate; recreates when in-place edit fails and component refs are provided.

| Field | Value |
|-------|-------|
| Worker command | `set_mate_limit_angle` |
| Tier | extended |
| Read only | false |
| Destructive | false |
| Confirm required | false |
| Description source | authored |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | yes | - | Assembly path under an allowed CAD root. (allowed root) |
| `mate_name` | string | yes | - | Existing LimitAngle mate feature name, e.g. LimitAngle9. |
| `min_angle_deg` | number | no | - | New minimum allowed rotation in degrees. |
| `max_angle_deg` | number | no | - | New maximum allowed rotation in degrees. |
| `angle_deg` | number | no | - | New current/nominal angle in degrees. |
| `flip` | boolean | no | - | Optional FlipDimension override. |
| `component_1` | string | no | - | First component name. Required to recreate if in-place edit fails. |
| `ref_1` | string | no | - | Reference on the first component for recreate fallback. |
| `component_2` | string | no | - | Second component name for recreate fallback. |
| `ref_2` | string | no | - | Reference on the second component for recreate fallback. |
| `axis_ref` | string | no | - | Optional rotation axis reference for recreate fallback. |
| `axis_component` | string | no | - | Component containing the rotation axis for recreate fallback. |
| `seed_angle_deg` | number | no | - | Seed angle for recreate fallback (defaults to angle_deg). |
| `check_branch_stability` | boolean | no | - | If true (default), suppress/unsuppress after update to detect opposite-branch pose flips. |

## Tags

- set

## Domains

- assembly
- mate

