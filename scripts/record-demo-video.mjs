/**
 * Record a demo video: Cursor-style prompt intro, then SolidWorks building
 * the demo part. Writes docs/assets/demo-build-part.{mp4,gif}.
 *
 * Prerequisites: SolidWorks open, ffmpeg on PATH, Microsoft Edge available.
 * Usage: npm run demo:record-video
 */
import { spawn, spawnSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const packageRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const assetsDir = path.join(packageRoot, "docs", "assets");
const promptHtml = path.join(assetsDir, "demo-cursor-prompt.html");
const introRaw = path.join(assetsDir, "demo-intro.raw.mkv");
const swRaw = path.join(assetsDir, "demo-sw.raw.mkv");
const introClip = path.join(assetsDir, "demo-intro.mp4");
const swClip = path.join(assetsDir, "demo-sw.mp4");
const concatList = path.join(assetsDir, "demo-concat.txt");
const mp4Path = path.join(assetsDir, "demo-build-part.mp4");
const gifPath = path.join(assetsDir, "demo-build-part.gif");
const thumbPath = path.join(assetsDir, "demo-build-part-thumb.png");

function ps(script) {
  const result = spawnSync(
    "powershell.exe",
    ["-NoProfile", "-Command", script],
    { encoding: "utf8", windowsHide: true },
  );
  if (result.status !== 0) {
    throw new Error(result.stderr || result.stdout || "PowerShell failed");
  }
  return (result.stdout || "").trim();
}

function ensureFfmpeg() {
  const which = spawnSync("where.exe", ["ffmpeg"], { encoding: "utf8", windowsHide: true });
  if (which.status !== 0) {
    throw new Error("ffmpeg not found on PATH. Install with: winget install Gyan.FFmpeg");
  }
}

function runFfmpeg(args, label) {
  const result = spawnSync("ffmpeg", args, { encoding: "utf8", windowsHide: true });
  if (result.status !== 0) {
    throw new Error(`${label} failed: ${(result.stderr || "").slice(-1000)}`);
  }
}

/**
 * Physical pixel bounds; gdigrab needs these on scaled 4K displays.
 * Optional place = { left, top, width, height } forces SetWindowPos first.
 */
function windowBounds(titleRegex, processRegex = ".", place = null) {
  const placeArg = place
    ? `${place.left},${place.top},${place.width},${place.height}`
    : "";
  const out = ps(`
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class WinB {
  public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("shcore.dll")] public static extern int SetProcessDpiAwareness(int v);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int nCmdShow);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
try { [void][WinB]::SetProcessDpiAwareness(2) } catch { [void][WinB]::SetProcessDPIAware() }
$titleRe = [regex]${JSON.stringify(titleRegex)}
$procRe = [regex]${JSON.stringify(processRegex)}
$place = ${JSON.stringify(placeArg)}
$script:best = $null
$cb = [WinB+EnumProc]{
  param($hWnd, $lParam)
  if (-not [WinB]::IsWindowVisible($hWnd)) { return $true }
  $sb = New-Object System.Text.StringBuilder 512
  [void][WinB]::GetWindowText($hWnd, $sb, $sb.Capacity)
  $title = $sb.ToString()
  if (-not $titleRe.IsMatch($title)) { return $true }
  [uint32]$procId = 0
  [void][WinB]::GetWindowThreadProcessId($hWnd, [ref]$procId)
  try { $proc = (Get-Process -Id $procId -ErrorAction Stop).ProcessName } catch { return $true }
  if (-not $procRe.IsMatch($proc)) { return $true }
  if ($place) {
    $parts = $place.Split(',')
    $x = [int]$parts[0]; $y = [int]$parts[1]; $cx = [int]$parts[2]; $cy = [int]$parts[3]
    [void][WinB]::ShowWindow($hWnd, 1) # SW_SHOWNORMAL
    [void][WinB]::SetWindowPos($hWnd, [IntPtr]::Zero, $x, $y, $cx, $cy, 0x0040)
    Start-Sleep -Milliseconds 150
  }
  $r = New-Object WinB+RECT
  [void][WinB]::GetWindowRect($hWnd, [ref]$r)
  $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
  if ($w -lt 400 -or $h -lt 300) { return $true }
  $dpi = [WinB]::GetDpiForWindow($hWnd)
  [void][WinB]::SetForegroundWindow($hWnd)
  if ($w % 2) { $w-- }
  if ($h % 2) { $h-- }
  $script:best = "$($r.Left),$($r.Top),$w,$h,$dpi,$title"
  return $false
}
[void][WinB]::EnumWindows($cb, [IntPtr]::Zero)
if (-not $script:best) { throw "No window matched title /$titleRe/ process /$procRe/" }
Write-Output $script:best
`);
  const [left, top, width, height, dpi, ...titleParts] = out.split(",");
  const title = titleParts.join(",");
  const bounds = {
    left: Number(left),
    top: Number(top),
    width: Number(width),
    height: Number(height),
    dpi: Number(dpi),
    title,
  };
  if (![bounds.left, bounds.top, bounds.width, bounds.height].every((n) => Number.isFinite(n))) {
    throw new Error(`Bad bounds: ${out}`);
  }
  return bounds;
}

function sleep(ms) {
  return new Promise((r) => setTimeout(r, ms));
}

async function recordRegion(bounds, outPath, seconds) {
  fs.rmSync(outPath, { force: true });
  console.error(
    `Recording ${bounds.width}x${bounds.height} @ ${bounds.left},${bounds.top} for ${seconds}s → ${path.basename(outPath)}`,
  );
  const proc = spawn(
    "ffmpeg",
    [
      "-y",
      "-f",
      "gdigrab",
      "-framerate",
      "15",
      "-offset_x",
      String(bounds.left),
      "-offset_y",
      String(bounds.top),
      "-video_size",
      `${bounds.width}x${bounds.height}`,
      "-i",
      "desktop",
      "-t",
      String(seconds),
      "-c:v",
      "libx264",
      "-pix_fmt",
      "yuv420p",
      "-preset",
      "ultrafast",
      "-crf",
      "23",
      outPath,
    ],
    { stdio: ["ignore", "ignore", "pipe"], windowsHide: true },
  );
  let err = "";
  proc.stderr.on("data", (c) => {
    err += c.toString();
  });
  const code = await new Promise((resolve) => proc.on("close", resolve));
  if (!fs.existsSync(outPath) || fs.statSync(outPath).size < 10_000) {
    throw new Error(`record failed (${code}): ${err.slice(-800)}`);
  }
}

function normalizeClip(input, output, duration) {
  runFfmpeg(
    [
      "-y",
      "-i",
      input,
      "-t",
      String(duration),
      "-vf",
      "scale=1280:720:force_original_aspect_ratio=decrease,pad=1280:720:(ow-iw)/2:(oh-ih)/2:color=0x181818,fps=15",
      "-c:v",
      "libx264",
      "-pix_fmt",
      "yuv420p",
      "-preset",
      "slow",
      "-crf",
      "26",
      "-movflags",
      "+faststart",
      "-an",
      output,
    ],
    `normalize ${path.basename(output)}`,
  );
}

function findEdge() {
  const candidates = [
    process.env.PROGRAMFILES && path.join(process.env.PROGRAMFILES, "Microsoft", "Edge", "Application", "msedge.exe"),
    process.env["PROGRAMFILES(X86)"] &&
      path.join(process.env["PROGRAMFILES(X86)"], "Microsoft", "Edge", "Application", "msedge.exe"),
  ].filter(Boolean);
  for (const candidate of candidates) {
    if (fs.existsSync(candidate)) return candidate;
  }
  const which = spawnSync("where.exe", ["msedge"], { encoding: "utf8", windowsHide: true });
  if (which.status === 0) {
    return which.stdout.trim().split(/\r?\n/)[0];
  }
  throw new Error("Microsoft Edge not found (needed for the Cursor prompt intro window)");
}

fs.mkdirSync(assetsDir, { recursive: true });
ensureFfmpeg();
if (!fs.existsSync(promptHtml)) {
  throw new Error(`Missing prompt HTML: ${promptHtml}`);
}

for (const stale of [introRaw, swRaw, introClip, swClip, concatList, mp4Path]) {
  fs.rmSync(stale, { force: true });
}

const edge = findEdge();
const htmlUrl = pathToFileURL(promptHtml).href;
const edgeProc = spawn(
  edge,
  [
    `--app=${htmlUrl}`,
    "--window-size=1400,900",
    "--window-position=240,140",
    "--force-device-scale-factor=1",
    "--high-dpi-support=1",
    "--disable-features=TranslateUI",
    "--no-first-run",
  ],
  { stdio: "ignore", windowsHide: false, detached: true },
);
edgeProc.unref();

let introBounds;
for (let attempt = 0; attempt < 20; attempt++) {
  await sleep(400);
  try {
    // Match Edge only — the live Cursor IDE often shares a similar title.
    // Force landscape size in physical pixels (150% DPI: 1400x900 logical ≈ 2100x1350).
    introBounds = windowBounds("SolidWorks MCP|demo prompt|demo-cursor-prompt", "msedge", {
      left: 360,
      top: 180,
      width: 2100,
      height: 1350,
    });
    break;
  } catch {
    // wait for Edge app window
  }
}
if (!introBounds) {
  ps(`
Get-Process msedge -ErrorAction SilentlyContinue | Where-Object {
  $_.MainWindowTitle -match 'SolidWorks MCP|demo prompt|demo-cursor-prompt'
} | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
`);
  throw new Error("Timed out waiting for Cursor prompt intro window");
}

// Restart the typing animation after the window is sized and focused.
ps(`
$wshell = New-Object -ComObject WScript.Shell
[void]$wshell.AppActivate(${JSON.stringify(introBounds.title)})
Start-Sleep -Milliseconds 200
$wshell.SendKeys('{F5}')
`);
await sleep(500);

await recordRegion(introBounds, introRaw, 7.5);

// Close the app-mode Edge window without killing the user's whole browser profile if possible.
ps(`
Get-Process msedge -ErrorAction SilentlyContinue | Where-Object {
  $_.MainWindowTitle -match 'SolidWorks MCP|demo prompt|demo-cursor-prompt'
} | ForEach-Object { $_.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 200; if (-not $_.HasExited) { Stop-Process -Id $_.Id -Force } }
`);

const swBounds = windowBounds("SOLIDWORKS|SolidWorks", "SLDWORKS");
const recordPromise = recordRegion(swBounds, swRaw, 24);

await sleep(1200);

const { runWorker } = await import("./lib/worker-client.mjs");
const demoDir = path.join(packageRoot, ".demo");
const partPath = path.join(demoDir, "DemoCube.SLDPRT");
fs.mkdirSync(demoDir, { recursive: true });
const roots = new Set([
  ...(process.env.SOLIDWORKS_MCP_ALLOWED_ROOTS || "").split(";").map((s) => s.trim()).filter(Boolean),
  demoDir,
  packageRoot,
]);
process.env.SOLIDWORKS_MCP_ALLOWED_ROOTS = [...roots].join(";");

let demoError = null;
try {
  const built = runWorker("demo_build_part", {
    size_mm: 40,
    hole_diameter_mm: 18,
    output_path: partPath,
    confirm: true,
  });
  console.error(JSON.stringify({ demo: "ok", cutFeatureNames: built.cutFeatureNames }, null, 2));
} catch (err) {
  demoError = err instanceof Error ? err.message : String(err);
  console.error(`demo_build_part warning: ${demoError}`);
}

await recordPromise;

normalizeClip(introRaw, introClip, 7.2);
normalizeClip(swRaw, swClip, 16);

const listBody = `file '${introClip.replace(/\\/g, "/")}'\nfile '${swClip.replace(/\\/g, "/")}'\n`;
fs.writeFileSync(concatList, listBody, "utf8");

runFfmpeg(
  ["-y", "-f", "concat", "-safe", "0", "-i", concatList, "-c", "copy", mp4Path],
  "concat",
);

// Keep GIF at 1280px with a full 256-color palette so chat prompt text stays readable.
runFfmpeg(
  [
    "-y",
    "-i",
    mp4Path,
    "-vf",
    [
      "fps=12",
      "scale=1280:-1:flags=lanczos",
      "unsharp=3:3:0.6:3:3:0.0",
      "split[s0][s1]",
      "[s0]palettegen=max_colors=256:stats_mode=full[p]",
      "[s1][p]paletteuse=dither=floyd_steinberg:new=1",
    ].join(","),
    "-loop",
    "0",
    gifPath,
  ],
  "gif",
);

runFfmpeg(["-y", "-ss", "00:00:14", "-i", mp4Path, "-frames:v", "1", "-update", "1", thumbPath], "thumb");

for (const temp of [introRaw, swRaw, introClip, swClip, concatList]) {
  fs.rmSync(temp, { force: true });
}

const summary = {
  ok: !demoError,
  demoError,
  introBounds,
  swBounds,
  mp4Path,
  gifPath,
  thumbPath,
  mp4Bytes: fs.statSync(mp4Path).size,
  gifBytes: fs.statSync(gifPath).size,
};
console.log(JSON.stringify(summary, null, 2));
if (demoError) process.exitCode = 1;
