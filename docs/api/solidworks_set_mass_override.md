# solidworks_set_mass_override

Set the assigned mass override on a part document in kilograms.

| Field | Value |
|-------|-------|
| Worker command | `set_mass_override` |
| Tier | extended |
| Read only | false |
| Destructive | true |
| Confirm required | true |
| Description source | authored |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | yes | - | Value for path. (allowed root) |
| `mass_kg` | number | yes | - | Value for mass kg. |
| `save` | boolean | yes | `false` | Value for save. |
| `confirm` | const `true` | yes | `true` | Value for confirm. |

## Tags

- set
- mass

## Domains

- document

