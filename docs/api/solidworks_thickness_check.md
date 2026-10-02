# solidworks_thickness_check

Wall-thickness analysis by ray casting: thinnest wall, percentiles, surface share below a minimum wall, and the locations of thin spots. Use with simulate_static to find weak or unprintable regions.

| Field | Value |
|-------|-------|
| Worker command | `thickness_check` |
| Tier | core |
| Read only | true |
| Destructive | false |
| Confirm required | false |
| Description source | authored |

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `path` | string | yes | - | Part document to edit (.SLDPRT). Required so edits never land in whichever document happens to be active. (allowed root) |
| `units` | `mm`, `cm`, `m`, `in` | no | - | Length units for every length and coordinate in this call. Default mm. |
| `min_wall` | number | no | - | Flag walls thinner than this (default 1.2 mm = 3 perimeters of a 0.4 mm nozzle). |
| `samples` | integer | no | - | Max surface samples (default 3000). |

## Tags

- inspect
- analysis
- 3d-printing

## Domains

- document

