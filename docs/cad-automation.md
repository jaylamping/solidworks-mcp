# CAD automation capabilities

SolidWorks MCP worker commands exposed via TypeScript tools. All paths must be under `SOLIDWORKS_MCP_ALLOWED_ROOTS` (default: Marengo repo).

## Layout-driven frame build

**Tool:** `marengo_torso_frame_build`  
**Worker:** `torso_frame_build_mates`

Pipeline for `marengo_torso_frame_asm`:

1. Show and fix layout jig (`marengo_torso_layout`).
2. Ensure `L100` depth-rail config on `vendor_2020_black_extrusion` (100 mm).
3. For each of 12 extrusions: translate to layout ICE center (`bottom_rail_*`, `top_rail_*`), attempt coincident/parallel mates, **fix component** if mates fail.
4. Place 16× `bracket_2028_corner` at **inside** joint corners (2 per corner, bottom + top bands).
5. Hide layout jig, save.

**Manual layout snap (destructive — opt-in only):**  
`node scripts/torso-frame-layout-place.mjs --confirm`  
Refuses to run without `--confirm`. Do **not** run from agents unless the user explicitly asked. Same for `torso-frame-autobuild.mjs --confirm` and MCP `marengo_torso_frame_build` with `confirm: true`.

**Constraint modes**

| Mode | When | Result |
|------|------|--------|
| `mates` | `AddMate5` succeeds | Normal SolidWorks mate features |
| `fixed` | Mate API returns no mate (current SW quirk) | Components fixed at layout-aligned transforms — stable for BOM/export |

Script: `node scripts/torso-frame-autobuild.mjs --confirm`

## Primitives (novel building blocks)

| Worker command | MCP tool | Purpose |
|----------------|----------|---------|
| `get_feature_box` | `solidworks_get_feature_box` | Assembly-space bbox for layout ICE / extrusion ends |
| `probe_feature_faces` | `solidworks_probe_feature_faces` | Face areas on ICE for mate targeting |
| `align_component_to_feature` | `solidworks_align_component_to_feature` | Translate part to layout feature center |
| `mate_coincident` / `mate_parallel` | `solidworks_mate_*` | Reference-based mates with selection marks |
| `list_configurations` | (script only) | List part configs |
| `add_configuration_copy` | (script only) | Copy `L085` → `L100` etc. |
| `set_dimension` | (script only) | Drive `D1@Boss-Extrude1` per config |
| `get_component_box` | (script only) | Component AABB in assembly |
| `transform_component` | (script only) | Apply translation to float component |

## Known limitation

`AddMate5` often returns `mateError: 0` with `mateCreated: false` even when `selectedCount: 2`. Investigate macro-recorded mate sequence vs. COM marks. Until fixed, frame build uses **fix-in-place** after ICE alignment.

## Next extensions

- ICE face mates via persistent face IDs / `GetSelectByIDString`
- Width mates between rail pairs for bracket slots
- `marengo_torso_asm_build` — frame sub-asm + RS03 to top-level layout
- Macro capture hook: record one manual mate → replay via worker
