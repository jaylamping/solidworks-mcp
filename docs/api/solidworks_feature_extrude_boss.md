# solidworks_feature_extrude_boss

Extrude the active or named sketch as a boss. Use merge:false for a separate tool body. merge_body_name merges into one solid without auto-selecting unrelated multi-body solids. use_feat_scope/use_auto_select default from merge.

| Field | Value |
|-------|-------|
| Worker command | `feature_extrude_boss` |
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
| `depth_m` | number | no | - | Value for depth m. |
| `merge` | boolean | no | - | When false, create a separate solid body (tool bodies, envelopes). Default true. |
| `flip` | boolean | no | - | Value for flip. |
| `merge_body_name` | string | no | - | When merge is true, append-select this solid so FeatureExtrusion does not auto-consume other multi-body solids. |
| `use_feat_scope` | boolean | no | - | Value for use feat scope. |
| `use_auto_select` | boolean | no | - | Defaults to merge && !merge_body_name. Set false with merge:false for reliable tool bodies. |
| `sketch_name` | string | no | - | Value for sketch name. |
| `confirm` | const `true` | yes | `true` | Value for confirm. |

## Tags

- feature

## Domains

- document

