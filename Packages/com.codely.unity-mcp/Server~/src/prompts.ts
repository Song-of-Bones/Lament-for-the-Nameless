import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";

const QUICKSTART = `You are connected to a Unity/Tuanjie editor through the Codely Unity MCP bridge.

Do this in order before touching anything:

1. Call project_overview. It tells you the engine version, render pipeline, active scene, build scenes,
   package set, asset counts, Play Mode state and console health.
2. Call project_map. It lists folder roles, scenes and their purpose, every custom component with its
   serialized fields, where each component is used, and the likely entry points. Do not browse the source tree.
3. Read resource unity://project/recipes. It contains verified call sequences for common requests
   (change UI text, swap a material, add a button, aim a camera, diagnose the console, find a feature owner).
4. Call tool_help with a tool name and action before using an unfamiliar action: it returns exact parameters,
   a copy-pasteable example, the response shape, and common errors.
5. Call list_tools (or read unity://project/tools) for the authoritative catalog. Do not guess action names.
6. Call manage_editor.get_state to confirm play/compile state.
7. Use manage_scene.get_hierarchy (key_objects gives camera/UI/manager refs), manage_gameobject.find or
   manage_asset.search to locate the target. Responses carry a ref object you can pass back as 'ref'.
8. Act with a dedicated tool. Prefer dedicated tools over exec_editor_script.
9. Verify: read_console (its diagnosis[] explains likely causes), get_state, manage_screenshot only for visuals.

Rules that prevent most failures:
- Paths are project-relative: Assets/Scenes/Main.unity.
- Scene-object targets accept a name, a hierarchy path (Root/Child), an instance id string, or a 'ref' object.
- Every response has _meta{play_mode, can_write, blockers}. If can_write is false, read blockers[].fix.
- play, stop, refresh, request_compile and package changes wait for completion and survive domain reloads.
  If a connection drops, retry; the bridge reconnects.
- Project-mutating calls are blocked in Play Mode with code "blocked_in_play_mode". Call manage_editor.stop first.
- After editing scripts or assets, call manage_editor.request_compile (or refresh), then read_console.
- If a call fails, read its code and suggestion before retrying. For field names, use get_components.field_schema.`;

const SAFE_EDIT = `Before any write to the Unity project, walk this checklist.

1. Confirm mode: manage_editor.get_state must show playMode = "stopped". Writes are rejected in Play Mode.
2. Confirm target: manage_scene.get_hierarchy or manage_gameobject.find. Duplicate names are common; prefer a
   hierarchy path or instance id.
3. Prefer idempotent actions: ensure_component / ensure_tag / ensure_layer / ensure_prefab_variant do nothing
   when the state already matches.
4. Check existing errors first: read_console with types ["error"] and count 20. Fixing what is already broken
   is usually cheaper than adding to it.
5. Make the smallest change, then verify:
   - read_console for new errors
   - manage_editor.get_state for scene dirty/compile state
   - a screenshot only when the question is visual
6. If a compile fails, the error carries line and column. Fix and recompile; do not stack unrelated changes.
7. Never delete or overwrite assets without first confirming the target path with manage_asset.get_info.`;

export function registerPrompts(server: McpServer): void {
  server.registerPrompt(
    "unity-quickstart",
    {
      title: "Get oriented in this Unity project",
      description:
        "A safe, ordered workflow for an agent that has never seen this Unity project: discover the project, discover the tools, inspect, act, verify."
    },
    async () => ({
      messages: [
        {
          role: "user",
          content: { type: "text", text: QUICKSTART }
        }
      ]
    })
  );

  server.registerPrompt(
    "unity-safe-edit",
    {
      title: "Safely edit the Unity project",
      description:
        "A pre-flight checklist to run before mutating scenes, objects, assets or code, plus the verification steps after."
    },
    async () => ({
      messages: [
        {
          role: "user",
          content: { type: "text", text: SAFE_EDIT }
        }
      ]
    })
  );

  server.registerPrompt(
    "unity-recipes",
    {
      title: "Do a common Unity task with a verified recipe",
      description:
        "Points the model at the verified task recipes (UI text, material swap, button, camera, console diagnosis, feature owner, animator) and explains how to adapt one to the user's actual objects."
    },
    async () => ({
      messages: [
        {
          role: "user",
          content: {
            type: "text",
            text:
              "Read resource unity://project/recipes first. Pick the recipe whose goal matches the request, " +
              "then substitute the example paths and targets with real values from project_overview, project_map, " +
              "manage_scene.get_hierarchy or manage_gameobject.find.\n\n" +
              "If no recipe matches, build your own from tool_help: ask for one tool + action at a time, send its " +
              "example, then replace the values. Verify each step before the next one.\n\n" +
              "Never invent an action name or a serialized field name: both are discoverable (list_tools, " +
              "get_components.field_schema, inspect_script.fields)."
          }
        }
      ]
    })
  );
}
