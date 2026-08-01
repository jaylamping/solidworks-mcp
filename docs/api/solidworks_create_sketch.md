# solidworks_create_sketch

Create sketch.

| Field | Value |
|-------|-------|
| Worker command | `create_sketch` |
| Tier | extended |
| Read only | false |
| Destructive | true |
| Confirm required | true |
| Description source | derived |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `use_selection` | boolean | no | - | Use the current SolidWorks selection instead of named references. |
| `selection_index` | integer | no | - | 1-based selection index when selecting a specific highlighted entity. |
| `path` | string | no | - | Optional document path under an allowed CAD root. (allowed root) |
| `plane_name` | string | no | - | Value for plane name. |
| `confirm` | const `true` | yes | `true` | Value for confirm. |

## Tags

- create

## Domains

- document

