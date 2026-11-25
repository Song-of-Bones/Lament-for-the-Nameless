import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { UnityClient } from "./unityClient.js";
import { recipesJson } from "./recipes.js";

/**
 * Resources are the "read the project without calling a tool" path: an MCP client can
 * attach them to context up front, which is exactly what a Unity-newcomer agent needs.
 */
export function registerResources(server: McpServer, client: UnityClient): void {
  server.registerResource(
    "project-map",
    "unity://project/map",
    {
      title: "Unity project map",
      description:
        "Semantic map: folder roles, scenes and their purpose, custom components with serialized fields and usage, namespaces, entry points.",
      mimeType: "application/json"
    },
    async (uri) => {
      try {
        const response = await client.callRead("project_map", {}, 120_000);
        return {
          contents: [
            {
              uri: uri.href,
              mimeType: "application/json",
              text: JSON.stringify(response.data, null, 2)
            }
          ]
        };
      } catch (error) {
        return {
          contents: [
            {
              uri: uri.href,
              mimeType: "application/json",
              text: JSON.stringify({ error: error instanceof Error ? error.message : String(error) }, null, 2)
            }
          ]
        };
      }
    }
  );

  server.registerResource(
    "task-recipes",
    "unity://project/recipes",
    {
      title: "Verified task recipes",
      description:
        "Ordered, verified call sequences for common requests: change UI text, swap a material, add a button, aim a camera, diagnose the console, find a feature owner, change an animator.",
      mimeType: "application/json"
    },
    async (uri) => ({
      contents: [
        {
          uri: uri.href,
          mimeType: "application/json",
          text: recipesJson()
        }
      ]
    })
  );

  server.registerResource(
    "project-overview",
    "unity://project/overview",
    {
      title: "Unity project overview",
      description:
        "Engine version, render pipeline, active scene, build scenes, package set, asset counts, play/compile state and console health.",
      mimeType: "application/json"
    },
    async (uri) => {
      try {
        const response = await client.callRead("project_overview", {}, 60_000);
        return {
          contents: [
            {
              uri: uri.href,
              mimeType: "application/json",
              text: JSON.stringify(response.data, null, 2)
            }
          ]
        };
      } catch (error) {
        return {
          contents: [
            {
              uri: uri.href,
              mimeType: "application/json",
              text: JSON.stringify({ error: error instanceof Error ? error.message : String(error) }, null, 2)
            }
          ]
        };
      }
    }
  );

  server.registerResource(
    "tool-catalog",
    "unity://project/tools",
    {
      title: "Bridge capability catalog",
      description:
        "Every tool, action, parameter, recommended call order and safety rule. Use it to plan a task before acting.",
      mimeType: "application/json"
    },
    async (uri) => {
      try {
        const response = await client.callRead("list_tools", {}, 30_000);
        return {
          contents: [
            {
              uri: uri.href,
              mimeType: "application/json",
              text: JSON.stringify(response.data, null, 2)
            }
          ]
        };
      } catch (error) {
        return {
          contents: [
            {
              uri: uri.href,
              mimeType: "application/json",
              text: JSON.stringify({ error: error instanceof Error ? error.message : String(error) }, null, 2)
            }
          ]
        };
      }
    }
  );

  server.registerResource(
    "scene-hierarchy",
    "unity://project/hierarchy",
    {
      title: "Loaded scene hierarchy",
      description: "Object tree of every loaded scene (depth 4, up to 1500 nodes).",
      mimeType: "application/json"
    },
    async (uri) => {
      try {
        const response = await client.callRead(
          "manage_scene",
          { action: "get_hierarchy", maxDepth: 4, maxNodes: 1500 },
          60_000
        );
        return {
          contents: [
            {
              uri: uri.href,
              mimeType: "application/json",
              text: JSON.stringify(response.data, null, 2)
            }
          ]
        };
      } catch (error) {
        return {
          contents: [
            {
              uri: uri.href,
              mimeType: "application/json",
              text: JSON.stringify({ error: error instanceof Error ? error.message : String(error) }, null, 2)
            }
          ]
        };
      }
    }
  );

  server.registerResource(
    "console-recent",
    "unity://project/console",
    {
      title: "Recent console messages",
      description: "Newest errors, warnings and logs captured since the bridge started.",
      mimeType: "application/json"
    },
    async (uri) => {
      try {
        const response = await client.callRead(
          "read_console",
          { action: "get", types: ["error", "warning", "log"], count: 40, format: "detailed", collapse: true },
          30_000
        );
        return {
          contents: [
            {
              uri: uri.href,
              mimeType: "application/json",
              text: JSON.stringify(response.data, null, 2)
            }
          ]
        };
      } catch (error) {
        return {
          contents: [
            {
              uri: uri.href,
              mimeType: "application/json",
              text: JSON.stringify({ error: error instanceof Error ? error.message : String(error) }, null, 2)
            }
          ]
        };
      }
    }
  );
}
