# solidworks_feature_details

Explain how an existing part is built: every feature with its driving dimensions (full names usable with solidworks_set_dimensions), definition parameters (end conditions, depths, radii, pattern counts), parent features, and each sketch's geometry, relations and constrained status. Start here when asked to review or improve a design.

| Field | Value |
|-------|-------|
| Worker command | `feature_details` |
| Tier | core |
| Read only | true |
| Destructive | false |
| Confirm required | false |
| Description source | authored |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | yes | - | Part document to edit (.SLDPRT). Required so edits never land in whichever document happens to be active. (allowed root) |
| `units` | `mm`, `cm`, `m`, `in` | no | - | Length units for every length and coordinate in this call. Default mm. |
| `features` | string[] | no | - | Only these features (default: the whole tree). |
| `include_sketches` | boolean | no | - | Include sketch geometry (default true). |
| `max_entities` | integer | no | - | Cap sketch entities per sketch (default 200). |
| `profile` | boolean | no | - | Diagnostics: include per-feature timing (ms) of each lookup. |

## Tags

- inspect
- modeling
- design

## Domains

- document

