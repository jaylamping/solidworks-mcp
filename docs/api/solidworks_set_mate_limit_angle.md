# solidworks_set_mate_limit_angle

Edit a limit-angle mate (min/max/angle/flip). Force-rebuilds and returns ok:false if any active mate fails health.

| Field | Value |
|-------|-------|
| Worker command | `set_mate_limit_angle` |
| Tier | extended |
| Read only | false |
| Destructive | false |
| Confirm required | false |
| Description source | derived |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | yes | - | Value for path. (allowed root) |
| `mate_name` | string | yes | - | Value for mate name. |
| `min_angle_deg` | number | no | - | Value for min angle deg. |
| `max_angle_deg` | number | no | - | Value for max angle deg. |
| `angle_deg` | number | no | - | Value for angle deg. |
| `flip_dimension` | boolean | no | - | Value for flip dimension. |

## Tags

- set

## Domains

- assembly
- mate

