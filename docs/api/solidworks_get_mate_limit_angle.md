# solidworks_get_mate_limit_angle

Read the minimum, maximum, and current angle of a named SolidWorks limit-angle mate.

| Field | Value |
|-------|-------|
| Worker command | `get_mate_limit_angle` |
| Tier | extended |
| Read only | true |
| Destructive | false |
| Confirm required | false |
| Description source | authored |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | yes | - | Value for path. (allowed root) |
| `mate_name` | string | yes | - | Value for mate name. |

## Tags

- get
- mate
- urdf

## Domains

- assembly
- mate

