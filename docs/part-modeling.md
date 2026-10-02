# Part modeling, analysis, and design improvement

The selector-driven modeling tools build parts from scratch, edit existing ones, and analyze them, without
relying on anything being pre-selected in the SolidWorks UI. Every call names its target part with `path`, so a
failed step can never spill into whatever document happens to be active.

## Building a part

1. `solidworks_new_document` with `output_path` (uses SolidWorks' default part template).
2. `solidworks_sketch` on a plane or planar face, with a batch of entities.
3. A feature tool consumes the sketch (or works on edges/faces directly).
4. Verify: every feature result includes the part's volume, body count, and bounding box.
   `solidworks_part_report` lists the feature tree with rebuild errors; `solidworks_render_view` saves a PNG.
5. `solidworks_save_document`, then `solidworks_export` (`stl`/`step`) for printing or CAM.

Features that rebuild with an error are deleted again (`rollback_on_error`, default true) and the call fails
with the SolidWorks error and "what's wrong" list, so the part stays clean and the call can simply be retried.

## Reviewing and improving an existing design

1. `solidworks_feature_details` — how the part is built: every feature with its driving dimensions (full names
   such as `D1@Boss-Extrude1`), definitions (end conditions, depths, radii, pattern counts), parents, and each
   sketch's geometry and constrained status.
2. Analyze:
   - `solidworks_simulate_static` — FEA: fixtures + forces/pressures on faces, printed-filament or metal
     materials; returns peak von Mises stress and where, peak displacement, factor of safety.
     Validated against beam theory (cantilever deflection within 1%, root stress within 5%).
   - `solidworks_thickness_check` — thinnest walls and where (ray casting).
   - `solidworks_print_check` — overhangs, bridges, bed contact, bed fit, mass for each print orientation.
3. Change it:
   - `solidworks_set_dimensions` — edit several dimensions in one rebuild; reverted if anything breaks.
   - `solidworks_rollback` (before/after a feature) to insert features mid-tree (e.g. a fillet before a shell),
     then `solidworks_rollback` with no arguments to roll forward. `solidworks_reorder_feature` moves features.
   - `solidworks_equations` — global variables driving dimensions (one `Wall` value driving shell, ribs, bosses).
   - Add ribs (`solidworks_rib`), fillets at stress risers, thicker walls, gussets, lightening pockets.
4. Re-run the same analysis and compare.

## Units

All lengths and coordinates are in `units` (default `mm`; also `cm`, `m`, `in`). Angles are degrees.

## Sketches

`solidworks_sketch` opens a sketch, adds entities with snapping/inference disabled (so geometry lands exactly
on the given coordinates), closes it, and reports `regionCount` (closed areas a boss/cut can use) and
`sketchFrameInModel` (sketch origin and axes in model space).

| Plane | sketch x | sketch y | normal |
|-------|----------|----------|--------|
| Front Plane | +X | +Y | +Z |
| Top Plane | +X | −Z | +Y |
| Right Plane | −Z | +Y | +X |

On faces or reference planes, pass `space: "model"` and give 3D model points; they are projected onto the
sketch plane. Entity types: `line`, `centerline`, `circle`, `arc` (center/start/end), `arc3`, `rectangle`,
`center_rectangle` (optional `corner_radius`), `polyline` (optional `closed`, `corner_radius`), `slot`,
`polygon` (`radius_is: "inscribed"` for across-flats sizing), `spline`, `point`, `ellipse`, `text`
(engrave/emboss), `convert_edges` and `offset_edges` (project model edges or a face boundary, optionally offset —
lips, gaskets, wall-following cutouts). For `offset_edges`, positive distances go outward from a face boundary and
negative inward; an array (`[-2.2, -3.4]`) makes nested offsets of the same edges, which extrude as a ring. The
projected originals become construction geometry. Any entity can be `construction: true`. Use `edit_sketch` to add
to an existing sketch.

## Selectors

Feature tools pick faces, edges, planes, axes and bodies with selectors (model coordinates, in `units`):

```jsonc
"Top Plane"                                         // named plane / axis / sketch / feature / point / helix
{"face":  {"near": [30, 4, 0], "normal": [0, 1, 0]}} // nearest face to a point (default max_distance 2 mm)
{"face":  {"near": [12, 13, 0], "type": "cylinder"}}
{"edge":  {"near": [4, 4, 0], "direction": [0, 0, 1]}}
{"edge":  {"near": [6, 20, 0], "type": "circle"}}
{"edges": {"direction": [0, 1, 0], "box": [49, 0, -16, 51, 4, 16]}} // every match
{"edges": {"on_face": {"near": [0, 30, 0], "normal": [0, 1, 0]}}}  // a face's boundary
{"near":  [50, 0, 5]}                               // in body lists: the body closest to a point
```

For bodies, `{near: [...]}` picks the body that contains the point, otherwise the body whose surface is closest.
Pick points inside a face (not on its boundary) and on an edge (its midpoint is ideal). When unsure, run
`solidworks_part_report` with `include_faces` / `include_edges` (optionally `face_filter` / `edge_filter`)
to list candidates with their centers, normals, axes and radii. Failed selections list the nearest candidates.
Body names change as features are added, so prefer `{near: [...]}` for bodies.

## Feature notes

- **extrude**: `mode` boss/cut; `end` blind, through_all, up_to_next, up_to_face (`up_to`), offset_from_face,
  mid_plane; `direction2`, `draft_deg` (inward taper unless `draft_outward`), `thin`, `start_offset`,
  `merge:false` for a separate body. Cuts go into the material by default; sketches on reference planes outside
  the body may need `reverse: true`.
- **revolve**: uses the sketch's single centerline unless `axis` is given. The profile must not cross the axis.
- **sweep**: closed `profile` sketch or `circular_profile_diameter`; `sweep_path` sketch, helix, or edges. The
  profile must sit at the path START. Helical paths use minimum-twist alignment automatically.
- **helix**: returns `startPoint` and `startTangent` — put thread/spring profiles there (on a plane through the
  axis and the start point). A thread = helix + 60° profile + `sweep` with `mode: "cut"`.
- **loft**: `profiles` in order (sketches on parallel or angled planes); optional `guides`.
- **shell**: `remove_faces` to open the part (omit for a closed hollow body).
- **hole** (printed parts): plain / `counterbore` / `countersink`, blind or through-all, many `positions`;
  exact diameters (heat-set inserts, dowels, bearings).
- **hole_wizard** (machined parts): ISO tapped (cosmetic thread), socket-head counterbore, flat-head countersink,
  clearance; sizes like `M3`, `M4`.
- **rib**: open line sketch on a plane through the part; tries the other material side automatically.
- **draft**: faces + neutral plane/face; propagates along tangent faces by default (filleted walls draft together).
- **thickness_check / print_check** work on parts that were never displayed (model tessellation fallback) and,
  in multi-body parts, measure each wall within its own body.
- **simulate_static** `bodies` isolates the part under study when the file also holds reference bodies (an
  imported actuator, fixtures); render_view takes the same `bodies` filter.
- **pattern_linear**: pass `vector` (intended direction); edge directions have an arbitrary sign, so the tool
  checks where instances landed and flips if needed. Works for features and bodies.
- **pattern_circular**: `axis` can be an axis name, a cylindrical face, or a circular edge.
- **mirror**: features or bodies about a plane or planar face.
- **ref_plane / ref_axis**: offset / angled / three-point planes; axes from cylinders, edges, two planes, two points.
- **insert_part + combine**: bring a vendor body (actuator, bearing) into a part and `subtract` it for an exact
  pocket, or design around it. **split_body** cuts a body (enclosure base/lid). **move_body** rotates then
  translates (two features, since SolidWorks applies only one per Move/Copy).

## Regression bench

`npm run bench:modeling` builds every part in `scripts/modeling-bench/` against a running SolidWorks and checks
volumes, body counts, region counts and result fields per step (exact values, `{approx, tol}`, `{min, max}`,
and `expect_error` for negative tests). Output goes to `.demo/bench/`; each part is closed afterwards.
`npm run bench:modeling -- <regex>` runs a subset.
