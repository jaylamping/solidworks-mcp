# solidworks_insert_part

Insert the solid bodies of another part (e.g. a vendor actuator or bearing), optionally rotated/translated, to design around it or subtract it for an exact pocket (solidworks_combine).

| Field | Value |
|-------|-------|
| Worker command | `insert_part` |
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
| `source` | string | yes | - | Part file whose bodies are inserted (e.g. a vendor actuator) to design around or subtract. |
| `configuration` | string | no | - | Value for configuration. |
| `import_planes` | boolean | no | - | Value for import planes. |
| `import_axes` | boolean | no | - | Value for import axes. |
| `rotate` | object | no | - | Value for rotate. |
| `translate` | number[] | no | - | Value for translate. |
| `name` | string | no | - | Rename the created feature. |

## Tags

- body
- modeling
- derived

## Domains

- document

