# solidworks_mirror_component

Mirror component.

| Field | Value |
|-------|-------|
| Worker command | `mirror_component` |
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
| `mirror_plane` | string | no | - | Value for mirror plane. |
| `confirm` | const `true` | yes | `true` | Value for confirm. |

## Tags

- mirror

## Domains

- document

