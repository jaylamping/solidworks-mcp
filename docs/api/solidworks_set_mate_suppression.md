# solidworks_set_mate_suppression

Suppress or unsuppress a named mate.

| Field | Value |
|-------|-------|
| Worker command | `set_mate_suppression` |
| Tier | extended |
| Read only | false |
| Destructive | false |
| Confirm required | false |
| Description source | authored |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | yes | - | Value for path. (allowed root) |
| `mate_name` | string | yes | - | Value for mate name. |
| `suppressed` | boolean | no | - | Value for suppressed. |

## Tags

- set

## Domains

- assembly
- mate

