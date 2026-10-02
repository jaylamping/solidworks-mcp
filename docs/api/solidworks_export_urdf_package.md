# solidworks_export_urdf_package

Export a validated CAD assembly and manifest into a versioned URDF package.

| Field | Value |
|-------|-------|
| Worker command | `export_urdf_package` |
| Tier | advanced |
| Read only | false |
| Destructive | false |
| Confirm required | false |
| Description source | authored |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | no | - | Value for path. (allowed root) |
| `assembly_path` | string | no | - | Value for assembly path. (allowed root) |
| `manifest_path` | string | no | - | Value for manifest path. |
| `manifest` | object | no | - | Value for manifest. |
| `confirm` | const `true` | no | `true` | Value for confirm. |

## Tags

- export
- urdf
- package

## Domains

- assembly
- document

