# solidworks_delete_mates_in_range

Delete mates in range.

| Field | Value |
|-------|-------|
| Worker command | `delete_mates_in_range` |
| Tier | extended |
| Read only | false |
| Destructive | true |
| Confirm required | true |
| Description source | derived |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | no | - | Value for path. (allowed root) |
| `min_number` | integer | no | - | Value for min number. |
| `max_number` | integer | no | - | Value for max number. |
| `save` | boolean | no | - | Value for save. |
| `confirm` | const `true` | yes | `true` | Value for confirm. |

## Tags

- delete

## Domains

- assembly
- mate

