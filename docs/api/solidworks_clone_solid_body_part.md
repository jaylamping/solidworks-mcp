# solidworks_clone_solid_body_part

Clone solid body part.

| Field | Value |
|-------|-------|
| Worker command | `clone_solid_body_part` |
| Tier | extended |
| Read only | false |
| Destructive | true |
| Confirm required | true |
| Description source | derived |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `source_part_path` | string | yes | - | Value for source part path. (allowed root) |
| `output_part_path` | string | yes | - | Value for output part path. (allowed root) |
| `assembly_path` | string | no | - | Value for assembly path. (allowed root) |
| `component_name` | string | no | - | Value for component name. |
| `save` | boolean | no | - | Value for save. |
| `confirm` | const `true` | yes | `true` | Value for confirm. |

## Tags

- clone

## Domains

- document

