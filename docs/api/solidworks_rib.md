# solidworks_rib

Rib/web from an open sketch line: stiffen brackets, bosses, and enclosure walls. Tries the other material side automatically if the first fails.

| Field | Value |
|-------|-------|
| Worker command | `rib` |
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
| `sketch` | string | yes | - | Open line/chain sketch, usually on a plane through the part. |
| `thickness` | number | yes | - | Value for thickness. |
| `side` | `both`, `one` | no | - | Thickness on both sides of the line (default) or one side. |
| `flip_side` | boolean | no | - | One-sided ribs: put the thickness on the other side. |
| `direction` | `parallel`, `normal` | no | - | Extend parallel to the sketch plane (default, classic web rib) or normal to it. |
| `flip_material` | boolean | no | - | Value for flip material. |
| `draft_deg` | number | no | - | Value for draft deg. |
| `draft_outward` | boolean | no | - | Value for draft outward. |
| `name` | string | no | - | Rename the created feature. |
| `rollback_on_error` | boolean | no | - | Delete the feature again if it rebuilds with an error (default true), so the part stays clean. |

## Tags

- feature
- modeling
- rib

## Domains

- document

