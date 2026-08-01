# solidworks_insert_component

Insert a part into an assembly from an allowed path.

| Field | Value |
|-------|-------|
| Worker command | `insert_component` |
| Tier | extended |
| Read only | false |
| Destructive | false |
| Confirm required | false |
| Description source | authored |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | yes | - | Value for path. (allowed root) |
| `part_path` | string | yes | - | Value for part path. (allowed root) |
| `name` | string | no | - | Value for name. |
| `configuration` | string | no | - | Value for configuration. |
| `save` | boolean | no | - | Value for save. |

## Tags

- insert

## Domains

- document

