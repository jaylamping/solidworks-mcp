# solidworks_set_custom_properties

Set custom properties on a part or assembly and optionally save it.

| Field | Value |
|-------|-------|
| Worker command | `set_custom_properties` |
| Tier | core |
| Read only | false |
| Destructive | false |
| Confirm required | false |
| Description source | authored |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | yes | - | Value for path. (allowed root) |
| `properties` | object | yes | - | Value for properties. |
| `save` | boolean | no | - | Value for save. |

## Tags

- set

## Domains

- document

