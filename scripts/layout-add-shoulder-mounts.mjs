import { runWorker } from "./lib/worker-client.mjs";



const LAYOUT_PART =
  "C:/code/marengo/hardware/cad/parts/marengo_torso_layout_revA.SLDPRT";


try {
  const data = runWorker("layout_add_shoulder_mounts", {
    path: LAYOUT_PART,
    save: true,
    replace_existing: true,
    inner_rail_mm: 55,
    outer_poke_mm: 95,
    depth_offset_mm: 0,
  });
  console.log(JSON.stringify({ ok: true, data }, null, 2));
} catch (error) {
  console.log(
    JSON.stringify(
      { ok: false, error: error instanceof Error ? error.message : String(error) },
      null,
      2,
    ),
  );
  process.exit(1);
}