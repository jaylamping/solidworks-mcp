# solidworks_equations

Create/update global variables and equations that drive dimensions (parametric designs: one wall-thickness variable driving many features); lists all equations with values.

| Field | Value |
|-------|-------|
| Worker command | `equations` |
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
| `globals` | object | no | - | Global variables: {"Wall": 2.4} (number in `units`) or {"Wall": "2.4mm"}. Existing ones are updated. |
| `links` | object | no | - | Drive dimensions/variables by expressions, e.g. {"D1@Boss-Extrude1": "\"Wall\" * 2"} (quote variable names). |
| `remove` | string[] | no | - | Delete equations by left-hand-side name. |

## Tags

- edit
- modeling
- parametric

## Domains

- document

