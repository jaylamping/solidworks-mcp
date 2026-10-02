# solidworks_pattern_linear

Linear pattern of features or bodies along an edge/axis/plane-normal direction, optionally in a second direction.

| Field | Value |
|-------|-------|
| Worker command | `pattern_linear` |
| Tier | core |
| Read only | false |
| Destructive | false |
| Confirm required | false |
| Description source | authored |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | yes | - | Part document to edit (.SLDPRT). Required so edits never land in whichever document happens to be active. (allowed root) |
| `units` | `mm`, `cm`, `m`, `in` | no | - | Length units for every length and coordinate in this call. Default mm. |
| `features` | string[] | no | - | Seed features to pattern. |
| `bodies` | array | no | - | Seed bodies to pattern (instead of features). |
| `direction` | unknown | yes | - | Direction reference: linear edge, axis, plane/planar face (uses its normal). |
| `count` | integer | yes | - | Total instances including the seed. |
| `spacing` | number | yes | - | Value for spacing. |
| `reverse` | boolean | no | - | Value for reverse. |
| `vector` | number[] | no | - | Intended pattern direction in model space, e.g. [0,0,1]. Edge directions have an arbitrary sign; with vector the tool checks where instances landed and flips if needed. Recommended. |
| `direction2` | object | no | - | Value for direction2. |
| `name` | string | no | - | Rename the created feature. |
| `rollback_on_error` | boolean | no | - | Delete the feature again if it rebuilds with an error (default true), so the part stays clean. |

## Tags

- feature
- modeling
- pattern

## Domains

- document

