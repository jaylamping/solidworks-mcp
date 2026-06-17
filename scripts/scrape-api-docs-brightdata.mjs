#!/usr/bin/env node
/**
 * BFS scrape SolidWorks API help via Bright Data Web Unlocker REST API.
 * Env: BRIGHTDATA_API_TOKEN, BRIGHTDATA_WEB_UNLOCKER_ZONE
 * Optional: DOCS_SCRAPE_LIMIT (default 50 for smoke), DOCS_SCRAPE_DELAY_MS (default 1000)
 */
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(__dirname, "..");
const OUT = path.join(ROOT, "docs/api-reference");
const PAGES = path.join(OUT, "pages");
const SEED =
  "https://help.solidworks.com/2026/english/api/sldworksapiprogguide/Welcome.htm?id=0";
const PATH_PREFIX = "/2026/english/api/sldworksapiprogguide/";

const token = process.env.BRIGHTDATA_API_TOKEN;
const zone = process.env.BRIGHTDATA_WEB_UNLOCKER_ZONE;
const limit = Number(process.env.DOCS_SCRAPE_LIMIT ?? "50");
const delayMs = Number(process.env.DOCS_SCRAPE_DELAY_MS ?? "1000");

function sleep(ms) {
  return new Promise((r) => setTimeout(r, ms));
}

function urlToFilename(url) {
  const u = new URL(url);
  let slug = u.pathname.replace(/^\/2026\/english\/api\/sldworksapiprogguide\//, "");
  if (!slug || slug.endsWith("/")) slug += "index";
  slug = slug.replace(/\.htm$/i, "").replace(/[?&=]/g, "_");
  return `${slug || "Welcome"}.md`;
}

function extractLinks(html, baseUrl) {
  const links = new Set();
  const re = /href=["']([^"']+)["']/gi;
  let m;
  while ((m = re.exec(html)) !== null) {
    try {
      const abs = new URL(m[1], baseUrl);
      if (abs.hostname === "help.solidworks.com" && abs.pathname.includes("sldworksapiprogguide")) {
        links.add(abs.href.split("#")[0]);
      }
    } catch {
      /* skip bad href */
    }
  }
  return [...links];
}

async function unlockerFetch(url, format = "raw") {
  const res = await fetch("https://api.brightdata.com/request", {
    method: "POST",
    headers: {
      Authorization: `Bearer ${token}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify({
      zone,
      url,
      format,
    }),
  });
  if (!res.ok) {
    const body = await res.text();
    throw new Error(`Bright Data ${res.status}: ${body.slice(0, 200)}`);
  }
  return res.text();
}

function loadState() {
  const p = path.join(OUT, "crawl-state.json");
  if (fs.existsSync(p)) return JSON.parse(fs.readFileSync(p, "utf8"));
  return { queue: [SEED], visited: {}, errors: [] };
}

function saveState(state) {
  fs.mkdirSync(OUT, { recursive: true });
  fs.writeFileSync(path.join(OUT, "crawl-state.json"), JSON.stringify(state, null, 2));
}

function saveErrors(errors) {
  fs.writeFileSync(path.join(OUT, "scrape-errors.json"), JSON.stringify(errors, null, 2));
}

async function main() {
  if (!token || !zone) {
    console.error(
      "Set BRIGHTDATA_API_TOKEN and BRIGHTDATA_WEB_UNLOCKER_ZONE. Run /bd-setup in Cursor or see docs/api-reference/README.md",
    );
    process.exit(1);
  }

  fs.mkdirSync(PAGES, { recursive: true });
  const state = loadState();
  let scraped = Object.keys(state.visited).length;

  while (state.queue.length > 0 && scraped < limit) {
    const batch = state.queue.splice(0, 5);
    for (const url of batch) {
      if (state.visited[url]) continue;
      try {
        const html = await unlockerFetch(url, "raw");
        const links = extractLinks(html, url);
        for (const link of links) {
          if (!state.visited[link] && !state.queue.includes(link)) {
            state.queue.push(link);
          }
        }
        const text = html
          .replace(/<script[\s\S]*?<\/script>/gi, "")
          .replace(/<style[\s\S]*?<\/style>/gi, "")
          .replace(/<[^>]+>/g, " ")
          .replace(/\s+/g, " ")
          .trim();
        const md = `# ${url}\n\nsource: brightdata\nscrapedAt: ${new Date().toISOString()}\n\n${text.slice(0, 50000)}\n`;
        fs.writeFileSync(path.join(PAGES, urlToFilename(url)), md);
        state.visited[url] = { source: "brightdata", scrapedAt: new Date().toISOString() };
        scraped++;
        console.log(`[${scraped}/${limit}] ${url}`);
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err);
        state.errors.push({ url, error: msg, at: new Date().toISOString() });
        console.error(`FAIL ${url}: ${msg}`);
      }
      saveState(state);
      await sleep(delayMs);
    }
  }

  saveErrors(state.errors);
  const discovered = [...new Set([...Object.keys(state.visited), ...state.queue])];
  fs.writeFileSync(
    path.join(OUT, "url-map.json"),
    JSON.stringify({ base: SEED, results: discovered, source: "brightdata-bfs" }, null, 2),
  );

  const missing = state.queue.filter((u) => !state.visited[u]);
  fs.writeFileSync(path.join(OUT, "missing-pages.json"), JSON.stringify(missing, null, 2));

  console.log(`Scraped ${scraped} pages. Queue remaining: ${state.queue.length}. Run npm run docs:normalize`);
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
