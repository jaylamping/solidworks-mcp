# solidworks_set_dimensions

Change one or more driving dimensions of an existing part (D1@Boss-Extrude1, D2@Sketch3, ...) in one rebuild; lengths in units, angles in degrees. Every change is reverted if the part fails to rebuild. Get names from solidworks_feature_details.

| Field | Value |
|-------|-------|
| Worker command | `set_dimensions` |
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
| `values` | object | yes | - | Map of full dimension names to new values, e.g. {"D1@Boss-Extrude1": 8, "D2@Sketch1": 30}. Lengths in `units`, angles in degrees. Names come from solidworks_feature_details. |
| `rollback_on_error` | boolean | no | - | Delete the feature again if it rebuilds with an error (default true), so the part stays clean. |

## Tags

- dimension
- modeling
- edit

## Domains

- document

