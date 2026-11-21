#!/usr/bin/env node
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { UnityClient } from "./unityClient.js";
import { registerTools } from "./tools.js";
import { registerResources } from "./resources.js";
import { registerPrompts } from "./prompts.js";
const VERSION = "0.1.0";
const server = new McpServer({
    name: "codely-unity-mcp",
    version: VERSION
});
const client = new UnityClient();
registerTools(server, client);
registerResources(server, client);
registerPrompts(server);
const transport = new StdioServerTransport();
await server.connect(transport);
// stdout is reserved for the MCP protocol; diagnostics go to stderr.
console.error(`[codely-unity-mcp] server ${VERSION} ready (stdio). Editor discovery is lazy on first tool call.`);
const shutdown = () => {
    client.close();
    process.exit(0);
};
process.on("SIGINT", shutdown);
process.on("SIGTERM", shutdown);
