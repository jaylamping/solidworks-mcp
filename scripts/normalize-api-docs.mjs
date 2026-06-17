#!/usr/bin/env node
/**
 * Normalize scraped API docs into index.json + interfaces/*.md
 */
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(__dirname, "..");
const OUT = path.join(ROOT, "docs/api-reference");
const PAGES = path.join(OUT, "pages");
const INTERFACES = path.join(OUT, "interfaces");

const NOISE = [
  /^loading js/i,
  /^is this page useful/i,
  /^print$/i,
  /^table of contents$/i,
  /^other version$/i,
  /^subscription services$/i,
  /^my\.solidworks$/i,
  /^learning resources$/i,
];

function stripNoise(text) {
  return text
    .split("\n")
    .filter((line) => {
      const t = line.trim();
      if (!t) return true;
      return !NOISE.some((re) => re.test(t));
    })
    .join("\n")
    .replace(/\n{3,}/g, "\n\n")
    .trim();
}

function parseMeta(content) {
  const source = content.match(/^source:\s*(.+)$/m)?.[1]?.trim() ?? "unknown";
  const url = content.match(/^url:\s*(.+)$/m)?.[1]?.trim();
  const scrapedAt = content.match(/^scrapedAt:\s*(.+)$/m)?.[1]?.trim();
  return { source, url, scrapedAt };
}

function guessInterfaceName(filename, content) {
  const fromContent = content.match(/\b(I[A-Z][A-Za-z0-9_]+)\s+Interface\b/);
  if (fromContent) return fromContent[1];
  const base = path.basename(filename, ".md");
  if (/^I[A-Z]/.test(base)) return base;
  return null;
}

function extractMembers(content) {
  const members = new Set();
  for (const m of content.matchAll(/\b([A-Z][a-zA-Z0-9_]+)\s+(?:Method|Property|method|property)\b/g)) {
    members.add(m[1]);
  }
  for (const m of content.matchAll(/^#{1,3}\s+([A-Z][a-zA-Z0-9_]+)\s*$/gm)) {
    if (m[1].length > 2) members.add(m[1]);
  }
  return [...members].sort();
}

function main() {
  if (!fs.existsSync(PAGES)) {
    console.error(`No pages dir: ${PAGES}`);
    process.exit(1);
  }

  fs.mkdirSync(INTERFACES, { recursive: true });
  const files = fs.readdirSync(PAGES).filter((f) => f.endsWith(".md"));
  const index = [];
  const byInterface = new Map();

  for (const file of files) {
    const full = path.join(PAGES, file);
    const raw = fs.readFileSync(full, "utf8");
    const meta = parseMeta(raw);
    const cleaned = stripNoise(raw);
    const iface = guessInterfaceName(file, cleaned);
    const members = extractMembers(cleaned);
    const relPath = path.relative(OUT, full);

    index.push({
      file,
      path: relPath,
      interface: iface,
      members,
      ...meta,
    });

    if (iface) {
      const existing = byInterface.get(iface) ?? [];
      existing.push(cleaned);
      byInterface.set(iface, existing);
    }
  }

  for (const [iface, chunks] of byInterface) {
    const body = `# ${iface}\n\n${chunks.join("\n\n---\n\n")}\n`;
    fs.writeFileSync(path.join(INTERFACES, `${iface}.md`), body.slice(0, 500000));
  }

  const discovered = fs.existsSync(path.join(OUT, "url-map.json"))
    ? JSON.parse(fs.readFileSync(path.join(OUT, "url-map.json"), "utf8"))
    : null;
  const discoveredUrls = discovered?.results ?? [];
  const scrapedUrls = index.map((e) => e.url).filter(Boolean);
  const missing = discoveredUrls.filter((u) => !scrapedUrls.includes(u));

  fs.writeFileSync(path.join(OUT, "index.json"), JSON.stringify({ generatedAt: new Date().toISOString(), entries: index }, null, 2));
  if (missing.length) {
    fs.writeFileSync(path.join(OUT, "missing-pages.json"), JSON.stringify(missing, null, 2));
  }

  console.log(`Normalized ${files.length} pages, ${byInterface.size} interfaces → ${INTERFACES}`);
}

main();
