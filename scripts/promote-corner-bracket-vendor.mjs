/**
 * Promote incoming 2020 corner bracket to vendor_2028_corner_bracket_vendor.SLDPRT
 * and rewire marengo_torso_asm instances + naming.
 *
 * Requires SolidWorks running on Windows.
 */
import { runWorker } from "./lib/worker-client.mjs";


const __dirname = path.dirname(fileURLToPath(import.meta.url));
const packageRoot = path.resolve(__dirname, "..");

const marengoRoot = process.env.MARENGO_ROOT
  ? path.resolve(process.env.MARENGO_ROOT)
  : path.resolve(packageRoot, "..", "marengo");

const INCOMING = path.join(marengoRoot, "cad/vendor/incoming/2020_corner_bracket.SLDPRT");
const CANONICAL = path.join(marengoRoot, "cad/vendor/vendor_2028_corner_bracket_vendor.SLDPRT");
const TORSO_ASM = path.join(marengoRoot, "cad/assemblies/marengo_torso_asm.SLDASM");

const vendorProps = {
  process: "purchase",
  material: "aluminum",
  revision: "vendor",
  owner: process.env.MARENGO_CAD_OWNER ?? "Joey Lamping",
};

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
  console.log("1) Save incoming â†’ canonical vendor part (SolidWorks SaveAs)");
  await mkdir(path.dirname(CANONICAL), { recursive: true });
  console.log(
    JSON.stringify(
      await runWorker({
        command: "export",
        args: { path: INCOMING, output_path: CANONICAL, start_if_missing: true },
      }),
      null,
      2,
    ),
  );

  console.log("2) Ensure custom properties on canonical part");
  console.log(
    JSON.stringify(
      await runWorker({
        command: "set_custom_properties",
        args: { path: CANONICAL, properties: vendorProps, save: true },
      }),
      null,
      2,
    ),
  );

  console.log("3) Replace assembly references (incoming â†’ canonical)");
  console.log(
    JSON.stringify(
      await runWorker({
        command: "replace_components_by_path",
        args: {
          path: TORSO_ASM,
          from_part_path: INCOMING,
          to_part_path: CANONICAL,
          configuration: "Default",
          save: false,
        },
      }),
      null,
      2,
    ),
  );

  console.log("4) Rename instances â†’ bracket_2028_corner (BOM id)");
  const renames = [];
  for (const prefix of ["2020_corner_bracket", "vendor_2028_corner_bracket_vendor"]) {
    for (let i = 0; i < 32; i++) {
      try {
        renames.push(
          await runWorker({
            command: "rename_component",
            args: { path: TORSO_ASM, from: prefix, to: "bracket_2028_corner" },
          }),
        );
      } catch {
        break;
      }
    }
  }
  console.log(JSON.stringify({ renamed: renames.length }, null, 2));

  console.log("5) Save torso assembly");
  console.log(
    JSON.stringify(await runWorker({ command: "save_document", args: { path: TORSO_ASM } }), null, 2),
  );

  console.log("\nDone. incoming/ file kept as archive; assembly uses vendor_2028_corner_bracket_vendor.");
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});