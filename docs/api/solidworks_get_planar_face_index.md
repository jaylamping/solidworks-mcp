# solidworks_get_planar_face_index

Get planar face index.

| Field | Value |
|-------|-------|
| Worker command | `get_planar_face_index` |
| Tier | extended |
| Read only | true |
| Destructive | false |
| Confirm required | false |
| Description source | derived |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | no | - | Value for path. (allowed root) |
| `component_name` | string | no | - | Value for component name. |
| `feature_name` | string | no | - | Value for feature name. |
| `use_selection` | boolean | no | - | Use the current SolidWorks selection instead of named references. |
| `selection_index` | integer | no | - | 1-based selection index when selecting a specific highlighted entity. |

## Tags

- get

## Domains

- document

