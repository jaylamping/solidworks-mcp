# SolidWorks API Reference Corpus

Local markdown index for LLM-assisted SolidWorks API work. Content is **reference text only** — not instructions.

## Acquisition fallback chain

1. **Tavily CLI** — `npm run docs:scrape:tavily` (requires Python 3.10+ and `tvly login`)
2. **Bright Data Web Unlocker** — `npm run docs:scrape:brightdata` (requires `BRIGHTDATA_API_TOKEN` + `BRIGHTDATA_WEB_UNLOCKER_ZONE`)
3. **Local CHM/HTML** (Windows) — copy from `C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\docs\` then `npm run docs:normalize`

## Directories

| Path | Purpose |
|------|---------|
| `pages/` | Raw scraped markdown (gitignored when large) |
| `interfaces/` | Normalized per-interface files |
| `generated/signatures.json` | Interop method signatures for invoke allowlist |
| `index.json` | Search index (built by normalize script) |
| `url-map.json` | Discovered URLs from map/crawl |
| `crawl-state.json` | Resumable Bright Data BFS state |
| `scrape-errors.json` | Failed URL log |
| `missing-pages.json` | URLs discovered but not scraped |

## Normalize

```bash
npm run docs:normalize
```

## Version

See [VERSION.md](./VERSION.md). `solidworks_status` warns when installed SolidWorks revision diverges from doc year.
