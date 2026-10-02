# solidworks_rename_feature

Rename a FeatureManager feature, including mates and reference geometry.

| Field | Value |
|-------|-------|
| Worker command | `rename_feature` |
| Tier | extended |
| Read only | false |
| Destructive | true |
| Confirm required | true |
| Description source | authored |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | no | - | Value for path. (allowed root) |
| `from_name` | string | yes | - | Value for from name. |
| `to_name` | string | yes | - | Value for to name. |
| `save` | boolean | yes | `false` | Value for save. |
| `confirm` | const `true` | yes | `true` | Value for confirm. |

## Tags

- rename
- feature

## Domains

- document

