# solidworks_align_component_to_feature

Align component to feature.

| Field | Value |
|-------|-------|
| Worker command | `align_component_to_feature` |
| Tier | extended |
| Read only | false |
| Destructive | false |
| Confirm required | false |
| Description source | derived |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | no | - | Value for path. (allowed root) |
| `layout_component` | string | no | - | Value for layout component. |
| `layout_feature` | string | no | - | Value for layout feature. |
| `target_component` | string | no | - | Value for target component. |
| `target_plane` | string | no | - | Value for target plane. |
| `use_selection` | boolean | no | - | Use the current SolidWorks selection instead of named references. |
| `selection_index` | integer | no | - | 1-based selection index when selecting a specific highlighted entity. |

## Tags

- align

## Domains

- document

