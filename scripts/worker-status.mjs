import { spawn } from "node:child_process";

const child = spawn(
  "dotnet",
  ["run", "--project", "workers/SolidWorksComWorker/SolidWorksComWorker.csproj", "--no-launch-profile"],
  { stdio: ["pipe", "inherit", "inherit"], windowsHide: true },
);

child.stdin.end(`${JSON.stringify({ command: "status", args: { start_if_missing: false } })}\n`);
child.on("close", (code) => process.exit(code ?? 1));
