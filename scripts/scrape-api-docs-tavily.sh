#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BASE="https://help.solidworks.com/2026/english/api/sldworksapiprogguide/Welcome.htm?id=0"
OUT="$ROOT/docs/api-reference"

if ! command -v tvly >/dev/null 2>&1; then
  echo "Tavily CLI not found. Install: curl -fsSL https://cli.tavily.com/install.sh | bash && tvly login"
  echo "Requires Python 3.10+."
  exit 1
fi

mkdir -p "$OUT/pages"

echo "Mapping URL space..."
tvly map "$BASE" \
  --select-paths "/2026/english/api/sldworksapiprogguide/.*" \
  --max-depth 3 --limit 500 --json > "$OUT/url-map.json"

URL_COUNT=$(node -e "const j=require('$OUT/url-map.json'); console.log((j.results||j.urls||[]).length||0)" 2>/dev/null || echo 0)
if [ "$URL_COUNT" = "0" ]; then
  echo "Tavily map returned no URLs (likely bot protection). Try: npm run docs:scrape:brightdata"
  exit 1
fi

echo "Crawling to $OUT/pages ..."
tvly crawl "$BASE" \
  --select-paths "/2026/english/api/sldworksapiprogguide/.*" \
  --max-depth 4 --limit 2000 --extract-depth advanced --timeout 60 \
  --output-dir "$OUT/pages/"

echo "Done. Run: npm run docs:normalize"
