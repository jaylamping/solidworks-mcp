#!/usr/bin/env node
/**
 * Import SolidWorks API help from local CHM files (Windows + SW install).
 * Requires 7-Zip (7z.exe). Writes markdown to docs/api-reference/pages/.
 *
 * Env:
 *   SOLIDWORKS_INSTALL_DIR — default C:/Program Files/SOLIDWORKS Corp/SOLIDWORKS
 *   DOCS_7Z_PATH — path to 7z.exe
 *   DOCS_CHM_FILES — comma-separated extra CHM paths
 *   DOCS_CHM_SKIP — set 1 to skip extraction (reuse scripts/.chm-import cache)
 */
import fs from "node:fs";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(__dirname, "..");
const OUT = path.join(ROOT, "docs/api-reference");
const PAGES = path.join(OUT, "pages");
const CACHE = path.join(__dirname, ".chm-import");

const SW_ROOT =
  process.env.SOLIDWORKS_INSTALL_DIR?.replace(/\\/g, "/") ??
  "C:/Program Files/SOLIDWORKS Corp/SOLIDWORKS";
const API_DIR = path.join(SW_ROOT, "api");

const DEFAULT_CHMS = ["sldworksapi.chm", "sldworksapiprogguide.chm"];

function find7z() {
  if (process.env.DOCS_7Z_PATH && fs.existsSync(process.env.DOCS_7Z_PATH)) {
    return process.env.DOCS_7Z_PATH;
  }
  for (const p of [
    "C:/Program Files/7-Zip/7z.exe",
    "C:/Program Files (x86)/7-Zip/7z.exe",
  ]) {
    if (fs.existsSync(p)) return p;
  }
  return null;
}

function run7z(sevenZ, args) {
  const r = spawnSync(sevenZ, args, { encoding: "utf8", maxBuffer: 64 * 1024 * 1024 });
  if (r.status !== 0) {
    throw new Error(`7z failed: ${(r.stderr || r.stdout || "").slice(0, 400)}`);
  }
  return r.stdout;
}

function extractChm(sevenZ, chmPath, destDir) {
  fs.mkdirSync(destDir, { recursive: true });
  console.log(`Extracting ${path.basename(chmPath)} → ${destDir}`);
  run7z(sevenZ, ["x", "-y", `-o${destDir}`, chmPath]);
}

function decodeHtmlEntities(text) {
  return text
    .replace(/&nbsp;/gi, " ")
    .replace(/&amp;/gi, "&")
    .replace(/&lt;/gi, "<")
    .replace(/&gt;/gi, ">")
    .replace(/&quot;/gi, '"')
    .replace(/&#(\d+);/g, (_, n) => String.fromCharCode(Number(n)));
}

function htmlToMarkdown(html, sourcePath) {
  let title =
    html.match(/<span id="pagetitle"[^>]*>([^<]+)<\/span>/i)?.[1]?.trim() ??
    html.match(/<title>([^<]+)<\/title>/i)?.[1]?.trim() ??
    path.basename(sourcePath, path.extname(sourcePath));

  let body = html
    .replace(/<script[\s\S]*?<\/script>/gi, "")
    .replace(/<style[\s\S]*?<\/style>/gi, "")
    .replace(/<h1 class="heading"[^>]*>[\s\S]*?<\/h1>/gi, (block) => {
      const text = block.replace(/<[^>]+>/g, " ").replace(/\s+/g, " ").trim();
      return `\n\n## ${text}\n\n`;
    })
    .replace(/<h4 class="dxh4"[^>]*>([\s\S]*?)<\/h4>/gi, "\n\n#### $1\n\n")
    .replace(/<pre[^>]*>([\s\S]*?)<\/pre>/gi, (_, pre) => `\n\n\`\`\`\n${pre.replace(/<[^>]+>/g, "")}\n\`\`\`\n\n`)
    .replace(/<br\s*\/?>/gi, "\n")
    .replace(/<\/p>/gi, "\n\n")
    .replace(/<\/tr>/gi, "\n")
    .replace(/<\/li>/gi, "\n")
    .replace(/<[^>]+>/g, " ")
    .replace(/\r/g, "");

  body = decodeHtmlEntities(body).replace(/[ \t]+\n/g, "\n").replace(/\n{3,}/g, "\n\n").trim();

  return `# ${title}

source: chm
url: chm://${sourcePath.replace(/\\/g, "/")}
scrapedAt: ${new Date().toISOString()}

${body.slice(0, 100000)}
`;
}

function safeMdName(relHtml) {
  return relHtml
    .replace(/\\/g, "/")
    .replace(/\.html?$/i, ".md")
    .replace(/[^a-zA-Z0-9._~-]+/g, "_");
}

function walkHtmlFiles(dir, base = dir, out = []) {
  for (const ent of fs.readdirSync(dir, { withFileTypes: true })) {
    if (ent.name.startsWith("#") || ent.name.startsWith("$")) continue;
    const full = path.join(dir, ent.name);
    if (ent.isDirectory()) {
      walkHtmlFiles(full, base, out);
    } else if (/\.html?$/i.test(ent.name)) {
      out.push({ full, rel: path.relative(base, full) });
    }
  }
  return out;
}

function resolveChms() {
  const extra = process.env.DOCS_CHM_FILES
    ? process.env.DOCS_CHM_FILES.split(",").map((s) => s.trim()).filter(Boolean)
    : [];
  const names = [...new Set([...DEFAULT_CHMS.map((n) => path.join(API_DIR, n)), ...extra])];
  const found = names.filter((p) => fs.existsSync(p));
  if (found.length === 0) {
    throw new Error(
      `No CHM files under ${API_DIR}. Set SOLIDWORKS_INSTALL_DIR or DOCS_CHM_FILES.`,
    );
  }
  return found;
}

function main() {
  if (process.platform !== "win32") {
    console.error("CHM import is Windows-only (local SolidWorks install).");
    process.exit(1);
  }

  const sevenZ = find7z();
  if (!sevenZ) {
    console.error("7-Zip not found. Install 7-Zip or set DOCS_7Z_PATH.");
    process.exit(1);
  }

  const chms = resolveChms();
  console.log(`Using 7-Zip: ${sevenZ}`);
  console.log(`CHM sources: ${chms.map((c) => path.basename(c)).join(", ")}`);

  if (process.env.DOCS_CHM_SKIP !== "1") {
    if (fs.existsSync(CACHE)) fs.rmSync(CACHE, { recursive: true, force: true });
    fs.mkdirSync(CACHE, { recursive: true });
    for (const chm of chms) {
      const slug = path.basename(chm, ".chm");
      extractChm(sevenZ, chm, path.join(CACHE, slug));
    }
  } else if (!fs.existsSync(CACHE)) {
    console.error("DOCS_CHM_SKIP=1 but cache missing. Run without skip first.");
    process.exit(1);
  }

  fs.mkdirSync(PAGES, { recursive: true });
  for (const f of fs.readdirSync(PAGES)) {
    if (f.endsWith(".md")) fs.unlinkSync(path.join(PAGES, f));
  }

  const htmlFiles = [];
  for (const ent of fs.readdirSync(CACHE, { withFileTypes: true })) {
    if (ent.isDirectory()) {
      htmlFiles.push(...walkHtmlFiles(path.join(CACHE, ent.name), path.join(CACHE, ent.name)));
    }
  }

  const urlMap = { base: `chm://${API_DIR.replace(/\\/g, "/")}`, results: [], source: "local-chm" };
  let written = 0;

  for (const { full, rel } of htmlFiles) {
    const html = fs.readFileSync(full, "utf8");
    const mdName = safeMdName(path.basename(rel));
    const mdPath = path.join(PAGES, mdName);
    if (fs.existsSync(mdPath)) {
      const alt = safeMdName(rel.replace(/\\/g, "_"));
      fs.writeFileSync(path.join(PAGES, alt), htmlToMarkdown(html, full));
      urlMap.results.push(`chm://${full.replace(/\\/g, "/")}`);
    } else {
      fs.writeFileSync(mdPath, htmlToMarkdown(html, full));
      urlMap.results.push(`chm://${full.replace(/\\/g, "/")}`);
    }
    written++;
    if (written % 1000 === 0) console.log(`  … ${written} pages`);
  }

  fs.writeFileSync(path.join(OUT, "url-map.json"), JSON.stringify(urlMap, null, 2));
  fs.writeFileSync(
    path.join(OUT, "VERSION.md"),
    `# API documentation version

- **Target year:** 2026
- **Source:** local SolidWorks CHM (\`${API_DIR.replace(/\\/g, "/")}\`)
- **Last import:** ${new Date().toISOString()}
- **Pages:** ${written}

Match this to your installed SolidWorks year when possible.
`,
  );

  console.log(`Imported ${written} CHM pages → ${PAGES}`);
  console.log("Run: npm run docs:normalize");
}

main();
