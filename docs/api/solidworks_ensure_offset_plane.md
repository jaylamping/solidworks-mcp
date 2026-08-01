# solidworks_ensure_offset_plane

Ensure offset plane.

| Field | Value |
|-------|-------|
| Worker command | `ensure_offset_plane` |
| Tier | extended |
| Read only | false |
| Destructive | false |
| Confirm required | false |
| Description source | derived |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `part_path` | string | yes | - | Value for part path. (allowed root) |
| `plane_name` | string | yes | - | Value for plane name. |
| `offset_m` | number | yes | - | Value for offset m. |
| `reference_plane` | string | no | - | Value for reference plane. |
| `replace_existing` | boolean | no | - | Value for replace existing. |
| `save` | boolean | no | - | Value for save. |

## Tags

- ensure

## Domains

- document

