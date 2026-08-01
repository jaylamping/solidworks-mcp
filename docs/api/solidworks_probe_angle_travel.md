# solidworks_probe_angle_travel

Probe angular travel of a component about an axis within optional limits.

| Field | Value |
|-------|-------|
| Worker command | `probe_angle_travel` |
| Tier | extended |
| Read only | true |
| Destructive | false |
| Confirm required | false |
| Description source | authored |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | yes | - | Assembly path under an allowed CAD root. (allowed root) |
| `component_name` | string | yes | - | Component to move through the angle sweep. |
| `reference_component` | string | yes | - | Stationary component used as the angular reference. |
| `axis` | `x`, `y`, `z` | no | - | Rotation axis. |
| `angles_deg` | number[] | no | - | Explicit angles to probe in degrees. |
| `min_angle_deg` | number | no | - | Sweep minimum in degrees. |
| `max_angle_deg` | number | no | - | Sweep maximum in degrees. |
| `overshoot_deg` | number | no | - | Optional overshoot beyond each endpoint. |
| `tolerance_deg` | number | no | - | Angular comparison tolerance in degrees. |
| `restore` | boolean | no | - | Restore the original component position after probing. |

## Tags

- probe

## Domains

- document

