# solidworks_create_sketch

Legacy: open a sketch on a named plane and leave it active for the sketch_line/circle/rectangle tools. Prefer solidworks_sketch (one call, faces too, closes the sketch).

| Field | Value |
|-------|-------|
| Worker command | `create_sketch` |
| Tier | extended |
| Read only | false |
| Destructive | true |
| Confirm required | true |
| Description source | authored |

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

