import fs from "node:fs";
import path from "node:path";

import { packageRoot } from "../config.js";

export interface ApiDocIndexEntry {
  file: string;
  path: string;
  interface: string | null;
  members: string[];
  source?: string;
  url?: string;
  scrapedAt?: string;
}

export interface ApiDocIndex {
  generatedAt: string;
  entries: ApiDocIndexEntry[];
}

function apiReferenceRoot(): string {
  return path.join(packageRoot(), "docs/api-reference");
}

export function loadApiDocIndex(): ApiDocIndex | null {
  const indexPath = path.join(apiReferenceRoot(), "index.json");
  if (!fs.existsSync(indexPath)) return null;
  return JSON.parse(fs.readFileSync(indexPath, "utf8")) as ApiDocIndex;
}

function scoreEntry(entry: ApiDocIndexEntry, query: string): number {
  const q = query.toLowerCase();
  let score = 0;
  if (entry.interface?.toLowerCase().includes(q)) score += 10;
  if (entry.file.toLowerCase().includes(q)) score += 5;
  for (const m of entry.members) {
    if (m.toLowerCase().includes(q)) score += 3;
    if (m.toLowerCase() === q) score += 5;
  }
  if (entry.path.toLowerCase().includes(q)) score += 1;
  return score;
}

export function searchApiDocs(query: string, limit = 10): {
  query: string;
  versionFile: string | null;
  results: Array<{
    score: number;
    interface: string | null;
    file: string;
    members: string[];
    excerpt: string;
    source?: string;
    url?: string;
  }>;
} {
  const index = loadApiDocIndex();
  const versionPath = path.join(apiReferenceRoot(), "VERSION.md");
  const versionFile = fs.existsSync(versionPath) ? versionPath : null;

  if (!index) {
    return { query, versionFile, results: [] };
  }

  const ranked = index.entries
    .map((entry) => ({ entry, score: scoreEntry(entry, query) }))
    .filter((r) => r.score > 0)
    .sort((a, b) => b.score - a.score)
    .slice(0, limit);

  const results = ranked.map(({ entry, score }) => {
    const fullPath = path.join(apiReferenceRoot(), entry.path);
    let excerpt = "";
    if (fs.existsSync(fullPath)) {
      excerpt = fs.readFileSync(fullPath, "utf8").slice(0, 1200);
    } else {
      const ifacePath = entry.interface
        ? path.join(apiReferenceRoot(), "interfaces", `${entry.interface}.md`)
        : null;
      if (ifacePath && fs.existsSync(ifacePath)) {
        excerpt = fs.readFileSync(ifacePath, "utf8").slice(0, 1200);
      }
    }

    return {
      score,
      interface: entry.interface,
      file: entry.file,
      members: entry.members.slice(0, 20),
      excerpt,
      source: entry.source,
      url: entry.url,
    };
  });

  return { query, versionFile, results };
}

export function readInterfaceDoc(interfaceName: string): string | null {
  const p = path.join(apiReferenceRoot(), "interfaces", `${interfaceName}.md`);
  if (!fs.existsSync(p)) return null;
  return fs.readFileSync(p, "utf8");
}
