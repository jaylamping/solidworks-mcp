import { runWorker } from "./lib/worker-client.mjs";

const result = runWorker("status", { start_if_missing: false });
console.log(JSON.stringify({ ok: true, data: result }, null, 2));
