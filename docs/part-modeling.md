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
   - `solidworks_simulate_static` — FEA: fixtures + loads on faces (force, pressure, torque, and `remote`
     force/moment acting at a point — a load at the end of a lever, or an actuator's torque transmitted through a
     stiff flange), printed-filament or metal materials. Returns peak von Mises stress and displacement with
     locations, factor of safety, and `distribution.awayFromFixtures` — the peak excluding the singular stress at
     fixture edges, which is the number to compare between variants. `probes` report stress on the faces you
     are sizing (a fillet, a web) — add `mesh_controls` (finer elements on those faces) so the probe value is
     converged (a hole in a plate recovers Peterson's Kt within a few %); `plot` saves a contour image; `hotspots`
     runs SOLIDWORKS hot-spot diagnostics. For printed parts pass `build_direction` (the print's up vector): `layers`
     reports the tension pulling the layers apart against the interlayer strength (`layer_strength_factor`, default
     0.6 of the material strength) — run it for each candidate orientation and print the way that keeps the
     main bending stress along the layers.
     `analysis: "frequency"` gives natural frequencies, mass participation and a mode-shape image.
     `analysis: "topology"` runs a topology optimization: `topology.goal` stiffness (stiffest layout for
     `mass_reduction_percent`) or min_mass (lightest layout meeting `min_factor_of_safety` / `max_stress_mpa` /
     `max_displacement`), with `min_member_thickness`, `preserve` regions (bolt bosses, bearing seats) and
     `symmetry`. It saves the material plot (`plot.views`) — look at it, then redesign along the kept load paths
     (ribs/webs/pockets) and verify with a static study. Expect minutes per run on real parts (`max_iterations`
     bounds it; analyses get a 1 h worker timeout, `SOLIDWORKS_MCP_ANALYSIS_TIMEOUT_MS`).
     Validated against theory: cantilever deflection within 1%, stress away from the root within 1%, offset
     remote load within 0.2%, torsion twist and shear within 1%, first bending frequency within 0.3%.
   - `solidworks_thickness_check` — thinnest walls and where (ray casting).
   - `solidworks_measure_clearance` — gap between two sets of bodies or faces (closest points), overlap and
     interference volume, pass/fail against a required clearance. Pair it with `insert_part from_assembly` to check
     a part against its real neighbours.
   - `solidworks_render_view` with `section: {plane, offset}` — cut-away image to inspect bores, walls and pockets.
   - `solidworks_print_check` — overhangs, bridges, bed contact, bed fit, mass for each print orientation.
3. Change it:
   - `solidworks_set_dimensions` — edit several dimensions in one rebuild; reverted if anything breaks.
   - `solidworks_rollback` (before/after a feature) to insert features mid-tree (e.g. a fillet before a shell),
     then `solidworks_rollback` with no arguments to roll forward. `solidworks_reorder_feature` moves features.
   - `solidworks_equations` — global variables driving dimensions (one `Wall` value driving shell, ribs, bosses).
   - Add ribs (`solidworks_rib`), fillets at stress risers, thicker walls, gussets, lightening pockets.
4. Re-run the same analysis and compare — or let `solidworks_try_variants` do it: give it the analysis once and
   a list of variants (`dimensions` and/or `steps` such as a fillet or rib); it applies each, measures volume, mass,
   FEA (and optionally wall thickness), undoes it, and returns a side-by-side table plus `restored: true`.
   Use selectors that survive the change (`{faces: {normal: [1,0,0]}}` rather than a point on a face that moves).
5. Sketches created with `fully_define: true` get relations and named dimensions (with kinds such as `diameter`,
   `horlinear`), so the result stays parametric for later edits and equations.

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
- **sheet metal**: `solidworks_sheet_metal_base_flange` turns an open sketch profile into a bent bracket (a bend at
  every corner; the sketch is the inside of the bend) or a closed profile into a flat tab; `solidworks_flat_pattern`
  reports the blank size and bend count and writes a DXF for laser cutting.
- **fillet** also does full rounds (`full_round: {side1, center, side2}` — rounds a rib tip completely) and face
  fillets (`face_set1`/`face_set2`, blends faces that need not share an edge, e.g. a rib into a wall).
- **simulate_static loads**: `torque` applies tangential traction about `axis` and suits cylindrical faces
  (a bore, a shaft surface); to twist through a flat face such as a bolted actuator flange use
  `{type: "remote", point, moment_nm, connection: "rigid"}`, which matches torsion theory exactly.
- **simulate_static** `bodies` isolates the part under study when the file also holds reference bodies (an
  imported actuator, fixtures); render_view takes the same `bodies` filter.
- **pattern_linear**: pass `vector` (intended direction); edge directions have an arbitrary sign, so the tool
  checks where instances landed and flips if needed. Works for features and bodies.
- **pattern_circular**: `axis` can be an axis name, a cylindrical face, or a circular edge.
- **mirror**: features or bodies about a plane or planar face.
- **scale**: uniform or per-axis about the centroid or origin, all or selected bodies (shrinkage compensation,
  concept resizing).
- **ref_plane / ref_axis**: offset / angled / three-point planes; axes from cylinders, edges, two planes, two points.
- **insert_part + combine**: bring a vendor body (actuator, bearing) into a part and `subtract` it for an exact
  pocket, or design around it. With `from_assembly: {assembly, component}` the component is placed exactly where
  it sits in the assembly (this part's origin = the assembly origin), so a new bracket designed between two
  inserted actuators fits the real assembly; delete the reference bodies (`delete_body`) when done. **split_body** cuts a body (enclosure base/lid). **move_body** rotates then
  translates (two features, since SolidWorks applies only one per Move/Copy).

## Performance

Every call crosses a process boundary into SOLIDWORKS. The worker wraps each command in
`ISldWorks.CommandInProgress = true`, which stops SOLIDWORKS doing UI/idle work between API calls (about 14x
faster on large parts; disable with `SOLIDWORKS_MCP_COMMAND_IN_PROGRESS=0`). Sketch geometry is read with batch
calls. For parts that embed large vendor models, pass `bodies` to analysis tools and `body` / `of_feature` filters
to selectors so they do not scan thousands of foreign faces and edges.

## Regression bench

`npm run bench:modeling` builds every part in `scripts/modeling-bench/` against a running SolidWorks and checks
volumes, body counts, region counts and result fields per step (exact values, `{approx, tol}`, `{min, max}`,
and `expect_error` for negative tests). Output goes to `.demo/bench/`; each part is closed afterwards.
`npm run bench:modeling -- <regex>` runs a subset.
