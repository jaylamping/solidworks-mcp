import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";

import { searchApiDocs } from "../api-docs/search.js";
import { assertAllowedPath } from "../config.js";
import { errorResult, jsonResult } from "./common.js";

const searchSchema = z.object({
  query: z.string().min(1),
  limit: z.number().int().min(1).max(25).optional(),
});

export function registerDocsTools(server: McpServer): void {
  server.registerTool(
    "solidworks_search_api_docs",
    {
      title: "Search SolidWorks API docs",
      description:
        "Search local SolidWorks API reference markdown (from Tavily/Bright Data/CHM pipeline). Returns ranked excerpts.",
      inputSchema: searchSchema,
      annotations: { readOnlyHint: true },
    },
    async (args: z.infer<typeof searchSchema>) => {
      try {
        return jsonResult(searchApiDocs(args.query, args.limit ?? 10));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerResource(
    "api-reference-index",
    "solidworks://api-reference/index.json",
    {
      title: "SolidWorks API index",
      description: "Normalized API documentation index",
      mimeType: "application/json",
    },
    async () => {
      const { loadApiDocIndex } = await import("../api-docs/search.js");
      const index = loadApiDocIndex();
      return {
        contents: [
          {
            uri: "solidworks://api-reference/index.json",
            mimeType: "application/json",
            text: JSON.stringify(index ?? { entries: [] }, null, 2),
          },
        ],
      };
    },
  );
}
