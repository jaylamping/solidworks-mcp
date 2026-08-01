# solidworks_get_feature_box

Return the assembly-space bounding box for a named component feature.

| Field | Value |
|-------|-------|
| Worker command | `get_feature_box` |
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
| `feature_name` | string | no | - | Value for feature name. |
| `use_selection` | boolean | no | - | Use the current SolidWorks selection instead of named references. |
| `selection_index` | integer | no | - | 1-based selection index when selecting a specific highlighted entity. |

## Tags

- get

## Domains

- document

