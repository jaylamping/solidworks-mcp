/**
 * Apply Marengo custom properties to torso vendor parts (cad-conventions.json).
 * Requires SolidWorks running on Windows.
 *
 * Usage (from solidworks-mcp repo):
 *   npm run build
 *   node scripts/set-vendor-custom-props.mjs
 */
import { runWorker } from "./lib/worker-client.mjs";


const __dirname = path.dirname(fileURLToPath(import.meta.url));
const packageRoot = path.resolve(__dirname, "..");

const marengoRoot = process.env.MARENGO_ROOT
  ? path.resolve(process.env.MARENGO_ROOT)
  : path.resolve(packageRoot, "..", "marengo");

const owner = process.env.MARENGO_CAD_OWNER ?? "Joey Lamping";

const vendorProps = {
  process: "purchase",
  revision: "vendor",
  owner,
};

const targets = [
  {
    rel: "hardware/cad/vendor/vendor_robstride_rs03_vendor.SLDPRT",
    properties: { ...vendorProps, material: "RS03 actuator (vendor)" },
  },
  {
    rel: "hardware/cad/vendor/vendor_2020_black_extrusion.SLDPRT",
    properties: { ...vendorProps, material: "6063-T5" },
  },
  {
    rel: "hardware/cad/vendor/vendor_2028_corner_bracket_vendor.SLDPRT",
    properties: { ...vendorProps, material: "aluminum" },
  },
];

async function runWorker(request) {
  return new Promise((resolve, reject) => {
    const child = spawn(
      "dotnet",
      ["run", "--project", "workers/SolidWorksComWorker/SolidWorksComWorker.csproj", "--no-launch-profile"],
      { cwd: packageRoot, stdio: ["pipe", "pipe", "pipe"], windowsHide: true },
    );

    const stdout = [];
    const stderr = [];
    child.stdout.on("data", (chunk) => stdout.push(chunk));
    child.stderr.on("data", (chunk) => stderr.push(chunk));
    child.stdin.end(`${JSON.stringify(request)}\n`);

    child.on("close", (code) => {
      const out = Buffer.concat(stdout).toString("utf8").trim();
      const err = Buffer.concat(stderr).toString("utf8").trim();
      if (code !== 0) {
        reject(new Error(`worker exit ${code}${err ? `: ${err}` : ""}`));
        return;
      }
      try {
        const parsed = JSON.parse(out);
        if (!parsed.ok) {
          reject(new Error(parsed.error ?? "worker returned ok=false"));
          return;
        }
        resolve(parsed.data);
      } catch (e) {
        reject(new Error(`invalid worker JSON: ${out}\n${e}`));
      }
    });
  });
}

async function main() {
  console.log(`Marengo root: ${marengoRoot}`);
  for (const target of targets) {
    const filePath = path.join(marengoRoot, target.rel);
    console.log(`\n==> ${target.rel}`);
    const data = await runWorker({
      command: "set_custom_properties",
      args: { path: filePath, properties: target.properties, save: true },
    });
    console.log(JSON.stringify(data, null, 2));
  }
  console.log("\nDone. Re-run marengo_design_review on torso asm to verify.");
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});