# solidworks_replace_component_path

Replace component path.

| Field | Value |
|-------|-------|
| Worker command | `replace_component_path` |
| Tier | extended |
| Read only | false |
| Destructive | true |
| Confirm required | true |
| Description source | derived |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | yes | - | Value for path. (allowed root) |
| `component_name` | string | yes | - | Value for component name. |
| `to_part_path` | string | yes | - | Value for to part path. (allowed root) |
| `configuration` | string | no | - | Value for configuration. |
| `save` | boolean | no | - | Value for save. |
| `confirm` | const `true` | yes | `true` | Value for confirm. |

## Tags

- replace

## Domains

- document

