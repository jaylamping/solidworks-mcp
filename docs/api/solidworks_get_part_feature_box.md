# solidworks_get_part_feature_box

Get part feature box.

| Field | Value |
|-------|-------|
| Worker command | `get_part_feature_box` |
| Tier | extended |
| Read only | true |
| Destructive | false |
| Confirm required | false |
| Description source | derived |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `part_path` | string | no | - | Value for part path. (allowed root) |
| `path` | string | no | - | Value for path. (allowed root) |
| `feature_name` | string | no | - | Value for feature name. |
| `use_selection` | boolean | no | - | Use the current SolidWorks selection instead of named references. |
| `selection_index` | integer | no | - | 1-based selection index when selecting a specific highlighted entity. |

## Tags

- get

## Domains

- document

