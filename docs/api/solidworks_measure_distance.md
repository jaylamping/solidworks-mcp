# solidworks_measure_distance

Measure distance.

| Field | Value |
|-------|-------|
| Worker command | `measure_distance` |
| Tier | extended |
| Read only | true |
| Destructive | false |
| Confirm required | false |
| Description source | derived |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | no | - | Value for path. (allowed root) |
| `entity_a` | string | no | - | Value for entity a. |
| `entity_b` | string | no | - | Value for entity b. |
| `use_selection` | boolean | no | - | Use the current SolidWorks selection instead of named references. |
| `selection_index` | integer | no | - | 1-based selection index when selecting a specific highlighted entity. |

## Tags

- measure

## Domains

- document

