# solidworks_probe_feature_faces

List face count, area, and planarity for a component feature.

| Field | Value |
|-------|-------|
| Worker command | `probe_feature_faces` |
| Tier | extended |
| Read only | false |
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

- probe

## Domains

- document

