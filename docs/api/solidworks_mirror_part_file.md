# solidworks_mirror_part_file

Mirror part file.

| Field | Value |
|-------|-------|
| Worker command | `mirror_part_file` |
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
| `mirror_plane` | string | no | - | Value for mirror plane. |
| `save` | boolean | no | - | Value for save. |
| `confirm` | const `true` | yes | `true` | Value for confirm. |

## Tags

- mirror

## Domains

- document

