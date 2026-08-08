# solidworks_combine_bodies

Combine solid bodies (common/add/subtract). Common supports exactly two bodies and may be synthesized through subtract when InsertCombineFeature cannot create it. body_names[0] is the target/main body for subtract and common. With use_selection, highlight two bodies for dual selection via body_name (index 1) and tool_body_name (index 2). keep_body_names are copied and never consumed as synthesis subtract mains. Prefer body_names over feature_names for early features (Loft/Shell): Feature.GetFaces can mis-attribute ownership in multi-body parts.

| Field | Value |
|-------|-------|
| Worker command | `combine_bodies` |
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
| `operation` | `common`, `add`, `subtract` | yes | - | SolidWorks Combine op. common keeps only the intersection and may be synthesized with subtract for exactly two bodies when InsertCombineFeature cannot create it, add is union, subtract is main minus tools. |
| `body_name` | string | no | - | Primary/target body name. body_names[0] and this field are the main body for subtract and common. Filled from selection index 1 when use_selection is true. |
| `tool_body_name` | string | no | - | Second/tool body name. Filled from selection index 2 when use_selection is true. |
| `body_names` | string[] | no | - | Solid body names to combine. body_names[0] is the target/main body for subtract and common; the rest are tools. Prefer body_names over feature_names, especially for early Loft/Shell features. |
| `feature_names` | string[] | no | - | Feature names resolved to owning solid bodies and appended to the body list. Use body_names when possible. |
| `keep_body_names` | string[] | no | - | Bodies to preserve by copying before combine. Use when common/subtract would otherwise consume a body you still need (e.g. keep Loft body while trimming another body to it). |
| `keep_feature_names` | string[] | no | - | Features resolved to bodies and treated like keep_body_names. |
| `confirm` | const `true` | yes | `true` | Value for confirm. |

## Tags

- feature
- body
- boolean

## Domains

- document

