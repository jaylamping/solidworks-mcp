# solidworks_get_persist_reference

Capture a base64 persist reference for a component reference.

| Field | Value |
|-------|-------|
| Worker command | `get_persist_reference` |
| Tier | extended |
| Read only | true |
| Destructive | false |
| Confirm required | false |
| Description source | authored |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | no | - | Value for path. (allowed root) |
| `component_name` | string | no | - | Value for component name. |
| `ref` | string | no | - | Value for ref. |
| `face_index` | integer | no | - | Value for face index. |
| `use_selection` | boolean | no | - | Use the current SolidWorks selection instead of named references. |
| `selection_index` | integer | no | - | 1-based selection index when selecting a specific highlighted entity. |

## Tags

- get

## Domains

- document

