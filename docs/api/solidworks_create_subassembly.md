# solidworks_create_subassembly

Create subassembly.

| Field | Value |
|-------|-------|
| Worker command | `create_subassembly` |
| Tier | extended |
| Read only | false |
| Destructive | true |
| Confirm required | true |
| Description source | derived |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `output_path` | string | yes | - | Value for output path. (allowed root) |
| `component_path` | string | no | - | Value for component path. (allowed root) |
| `confirm` | const `true` | yes | `true` | Value for confirm. |

## Tags

- create

## Domains

- document

