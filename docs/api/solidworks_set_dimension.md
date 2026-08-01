# solidworks_set_dimension

Set a driving part dimension value in meters.

| Field | Value |
|-------|-------|
| Worker command | `set_dimension` |
| Tier | extended |
| Read only | false |
| Destructive | false |
| Confirm required | false |
| Description source | authored |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | yes | - | Value for path. (allowed root) |
| `dimension` | string | yes | - | Value for dimension. |
| `value_meters` | number | yes | - | Value for value meters. |
| `configuration` | string | no | - | Value for configuration. |

## Tags

- set

## Domains

- document

