import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { UnityClient, UnityToolError, type UnityResponse } from "./unityClient.js";

const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

const SUGGESTIONS: Record<string, string> = {
  blocked_in_play_mode:
    "Call manage_editor.stop first (it discards runtime state), then retry. Read-only tools, screenshots and manage_gameview still work in Play Mode.",
  unknown_action: "Call list_tools to see the valid actions and parameters for this tool.",
  unknown_tool_type: "Call list_tools to get the authoritative tool list.",
  object_not_found:
    "Call manage_scene.get_hierarchy, or manage_gameobject.find with a partial searchTerm, to locate the object first.",
  scene_not_found: "Call manage_asset.search with filterType 'Scene' to list scene paths, then pass an explicit path.",
  component_not_found: "Call manage_gameobject.get_components to list component type names on the target.",
  property_not_found:
    "Call manage_gameobject.get_components to list serialized property names, then reuse one of those names exactly.",
  asset_not_found: "Call manage_asset.search to find the exact asset path under Assets/.",
  type_not_found: "The type name may be project-specific or misspelled; try the short class name.",
  bridge_not_found:
    "Open the Unity/Tuanjie editor with 'com.codely.unity-mcp' installed, or set UNITY_MCP_PROJECT to the project root.",
  bridge_not_ready: "A domain reload is probably in progress; wait a few seconds and retry.",
  connection_lost:
    "A domain reload most likely happened. Retry read-only calls; before replaying a write, re-check state with manage_editor.get_state.",
  timeout: "Long editor operations may still be running. Poll manage_editor.get_operation.",
  operation_timeout:
    "The editor is still busy. Poll manage_editor.get_operation; do not start the same operation twice.",
  compile_error: "Read data.errors for line/column details, fix the script, then retry.",
  script_error: "The script ran but threw. Read data.stackTrace and adjust the code.",
  not_supported: "This action is not implemented in this build; see list_tools for supported actions.",
  path_outside_assets: "Writes must target paths under Assets/. Pass a path such as Assets/Scenes/Main.unity.",
  play_mode_active: "Stop Play Mode before editing project state."
};

type Content =
  | { type: "text"; text: string }
  | { type: "image"; data: string; mimeType: string };

type ToolResult = { content: Content[]; isError?: boolean };

function formatData(data: unknown): string {
  if (data == null) return "";
  try {
    return JSON.stringify(data, null, 2);
  } catch {
    return String(data);
  }
}

function okResult(response: UnityResponse, note?: string): ToolResult {
  const data = response.data as Record<string, unknown> | undefined;
  const content: Content[] = [];

  if (data && typeof data.image_base64 === "string") {
    content.push({ type: "image", data: data.image_base64, mimeType: String(data.mime_type ?? "image/png") });
    const { image_base64, ...rest } = data;
    content.push({
      type: "text",
      text: `${response.message ?? "OK"}${note ? `\n${note}` : ""}\n${formatData(rest)}`
    });
    return { content };
  }

  content.push({
    type: "text",
    text: `${response.message ?? "OK"}${note ? `\n${note}` : ""}${data !== undefined ? "\n" + formatData(data) : ""}`
  });
  return { content };
}

function errorResult(error: unknown): ToolResult {
  if (error instanceof UnityToolError) {
    const suggestion = SUGGESTIONS[error.code];
    const payload = {
      code: error.code,
      message: error.message,
      ...(suggestion ? { suggestion } : {}),
      ...(error.payload !== undefined ? { details: error.payload } : {})
    };
    return { content: [{ type: "text", text: formatData(payload) }], isError: true };
  }

  const message = error instanceof Error ? error.message : String(error);
  return {
    content: [{ type: "text", text: formatData({ code: "client_error", message }) }],
    isError: true
  };
}

function guarded<T>(fn: (args: T) => Promise<ToolResult>): (args: T) => Promise<ToolResult> {
  return async (args: T) => {
    try {
      return await fn(args);
    } catch (error) {
      return errorResult(error);
    }
  };
}

/** Polls the editor until a "started" operation reports done, then attaches console health. */
async function finishOperation(
  client: UnityClient,
  response: UnityResponse,
  options: { timeoutMs?: number; console?: "none" | "errors" | "full" } = {}
): Promise<UnityResponse> {
  const data = response.data as Record<string, unknown> | undefined;
  if (!data || data.status !== "started") return response;

  const deadline = Date.now() + (options.timeoutMs ?? 300_000);
  let last: Record<string, unknown> | null = null;

  while (Date.now() < deadline) {
    await sleep(600);
    try {
      const operation = await client.callRead("manage_editor", { action: "get_operation" }, 15_000);
      last = operation.data as Record<string, unknown>;
      if (last?.state === "done") {
        const merged: Record<string, unknown> = { ...data, status: "done", operation: last };
        if (options.console !== "none") {
          try {
            const console = await client.callRead(
              "read_console",
              {
                action: "get",
                types: ["error", "warning"],
                count: 20,
                format: "plain",
                collapse: true
              },
              15_000
            );
            merged.console = console.data;
          } catch {
            merged.console = { note: "Console read failed after the operation; call read_console directly." };
          }
        }
        return {
          ...response,
          message: `${response.message ?? "Operation started"} Completed.`,
          data: merged
        };
      }
      if (last?.state === "failed") {
        throw new UnityToolError(
          "operation_failed",
          String(last.hint ?? "The editor operation failed."),
          last
        );
      }
    } catch (error) {
      if (error instanceof UnityToolError) {
        if (["connection_lost", "bridge_not_ready", "bridge_connect_failed", "timeout"].includes(error.code)) continue;
        throw error;
      }
      throw error;
    }
  }

  throw new UnityToolError(
    "operation_timeout",
    "The editor operation did not report done before the deadline. It may still be running.",
    last
  );
}

const actionArg = (actions: string, description: string) =>
  z
    .string()
    .optional()
    .describe(`One of: ${actions}. ${description} If omitted, the tool's default action is used.`);

const vec3 = z
  .union([z.array(z.number()), z.object({ x: z.number(), y: z.number(), z: z.number() }), z.string()])
  .describe("[x,y,z] (or {x,y,z}, or 'x,y,z').");

export function registerTools(server: McpServer, client: UnityClient): void {
  server.registerTool(
    "project_overview",
    {
      title: "Project overview",
      description:
        "START HERE. Returns a compact briefing of the open Unity project: engine version, render pipeline, active scene, build scenes, loaded hierarchy size, packages, asset counts, play/compile state and console health. Call this first in a new session, or whenever you need to understand what the project is before acting.",
      inputSchema: {
        includeAssetCounts: z.boolean().optional().describe("Count scenes/scripts/prefabs/materials (default true)."),
        force: z.boolean().optional().describe("Bypass the 20s cache.")
      }
    },
    guarded(async (args) => okResult(await client.callRead("project_overview", args, 60_000)))
  );

  server.registerTool(
    "list_tools",
    {
      title: "List bridge capabilities",
      description:
        "Authoritative catalog of every tool, action and parameter the bridge supports, plus the recommended call order and safety rules. Call this when unsure which tool or action to use, or before a multi-step task.",
      inputSchema: {}
    },
    guarded(async () => okResult(await client.callRead("list_tools", {}, 30_000)))
  );

  server.registerTool(
    "manage_editor",
    {
      title: "Editor state and lifecycle",
      description:
        "Read editor state, enter/leave Play Mode, refresh assets, compile scripts, wait for idle, and manage tags/layers/windows/selection. Long operations (play, stop, refresh, request_compile) return only after they finish and survive domain reloads.",
      inputSchema: {
        action: actionArg(
          "get_state, play, stop, pause, resume, step, get_operation, refresh, request_compile, get_compilation_summary, wait_for_idle, get_tags, ensure_tag, add_tag, remove_tag, get_layers, ensure_layer, add_layer, remove_layer, get_windows, focus_window, get_selection, get_active_tool, set_active_tool, get_project_root",
          "Start with get_state."
        ),
        targetPaused: z.boolean().optional().describe("pause: desired paused state; omit to toggle."),
        singleFrame: z.boolean().optional().describe("resume: true = advance one frame."),
        clearCache: z.boolean().optional().describe("request_compile: full clean rebuild."),
        console_mode: z.enum(["none", "errors", "full"]).optional().describe("Console detail returned after refresh/compile."),
        tagName: z.string().optional().describe("Tag name for tag actions."),
        layerName: z.string().optional().describe("Layer name for layer actions."),
        windowType: z.string().optional().describe("Window type for focus_window, e.g. SceneView or GameView."),
        toolName: z.string().optional().describe("Move|Rotate|Scale|View|Transform for set_active_tool."),
        timeoutSeconds: z.number().int().positive().optional().describe("Client budget hint in seconds.")
      }
    },
    guarded(async (args) => {
      const action = args.action ?? "get_state";
      const response = await client.call(
        "manage_editor",
        args as Record<string, unknown>,
        action === "refresh" || action === "request_compile" ? 60_000 : 30_000
      );
      const finished = await finishOperation(client, response, {
        console: args.console_mode ?? "errors",
        timeoutMs: (args.timeoutSeconds ?? 300) * 1000
      });
      return okResult(finished);
    })
  );

  server.registerTool(
    "manage_scene",
    {
      title: "Scenes and hierarchy",
      description:
        "Open, save or create scene files, and read the hierarchy of loaded scenes. The hierarchy is the map you need before touching objects; it is capped by depth and node count on large scenes.",
      inputSchema: {
        action: actionArg("load, save, create, get_hierarchy", "get_hierarchy is the safe default."),
        name: z.string().optional().describe("Scene name (extension optional) or save-as name."),
        path: z.string().optional().describe("Directory under Assets/, or an explicit .unity path."),
        buildIndex: z.number().int().min(0).optional().describe("Build-settings index instead of name/path."),
        maxDepth: z.number().int().min(1).max(32).optional().describe("Hierarchy depth limit (default 6)."),
        maxNodes: z.number().int().min(1).max(20000).optional().describe("Hierarchy node limit (default 2000).")
      }
    },
    guarded(async (args) => okResult(await client.call("manage_scene", args as Record<string, unknown>, 60_000)))
  );

  server.registerTool(
    "manage_gameobject",
    {
      title: "Scene objects",
      description:
        "Find, create, modify and delete scene objects; inspect and edit components and children. Writes use Undo and mark the scene dirty. Blocked in Play Mode — call manage_editor.stop first.",
      inputSchema: {
        action: actionArg(
          "find, create, modify, delete, get_components, remove_component, set_component_property, list_children, ensure_component, ensure_renderer_material, ensure_mesh_collider_mesh, ensure_prefab_default_sprite",
          "Use find before any write."
        ),
        target: z.string().optional().describe("Name, hierarchy path (Root/Child), or instance id."),
        ref: z.any().optional().describe("A ref object returned by an earlier call (objects[].ref). Replaces target."),
        searchTerm: z.string().optional().describe("Partial, case-insensitive name filter."),
        searchMethod: z.enum(["by_name", "by_path", "by_id"]).optional(),
        searchInactive: z.boolean().optional().describe("Include inactive objects (default true)."),
        findAll: z.boolean().optional().describe("Return every match instead of the first."),
        name: z.string().optional().describe("Object name (create) or new name (modify)."),
        primitiveType: z.enum(["cube", "sphere", "capsule", "cylinder", "plane", "quad"]).optional(),
        parent: z.string().optional().describe("Parent name/path; empty string moves to root."),
        position: vec3.optional(),
        rotation: vec3.optional().describe("Euler degrees."),
        scale: vec3.optional(),
        tag: z.string().optional(),
        layer: z.number().int().min(0).max(31).optional(),
        setActive: z.boolean().optional(),
        componentsToAdd: z.any().optional().describe("Array of {type, properties}."),
        componentsToRemove: z.any().optional().describe("Array of component type names."),
        componentProperties: z.any().optional().describe("Object: component type => {property: value}."),
        componentName: z.string().optional().describe("Component type name for component actions."),
        component_type: z.string().optional().describe("ensure_component: component type name."),
        propertyName: z.string().optional(),
        propertyValue: z.any().optional(),
        material: z.string().optional().describe("Material name or asset path."),
        mesh: z.string().optional().describe("Optional mesh asset path."),
        prefab: z.string().optional().describe("Prefab asset path for ensure_prefab_default_sprite."),
        sprite: z.string().optional().describe("Sprite asset path."),
        saveAsPrefab: z.boolean().optional(),
        prefabPath: z.string().optional(),
        includeNonPublicSerialized: z.boolean().optional(),
        limit: z.number().int().positive().optional().describe("find: max results (default 100).")
      }
    },
    guarded(async (args) => okResult(await client.call("manage_gameobject", args as Record<string, unknown>, 60_000)))
  );

  server.registerTool(
    "manage_asset",
    {
      title: "Assets",
      description:
        "Search and inspect assets, create or modify materials/ScriptableObjects, and create prefab variants. Asset paths are project-relative and start with Assets/.",
      inputSchema: {
        action: actionArg("search, get_info, create, modify, ensure_prefab_variant", "search is the safe default."),
        path: z.string().optional().describe("Folder or asset path under Assets/."),
        searchPattern: z.string().optional().describe("File-name substring."),
        filterType: z.string().optional().describe("Type name, e.g. Material, Prefab, Texture2D, Scene."),
        filterDateAfter: z.string().optional().describe("ISO date; only newer assets."),
        pageSize: z.number().int().min(1).max(200).optional(),
        pageNumber: z.number().int().min(1).optional(),
        generatePreview: z.boolean().optional(),
        assetType: z.string().optional().describe("create: Material or a ScriptableObject type name."),
        properties: z.any().optional().describe("Property values for create/modify."),
        sourcePrefab: z.string().optional().describe("ensure_prefab_variant: source prefab path.")
      }
    },
    guarded(async (args) => okResult(await client.call("manage_asset", args as Record<string, unknown>, 60000)))
  );

  server.registerTool(
    "read_console",
    {
      title: "Console",
      description:
        "Read or clear Unity console messages with severity filters, substring filtering and optional grouping. Call after compile/refresh/play, or whenever something looks wrong: errors usually say what to fix next.",
      inputSchema: {
        action: actionArg("get, clear, set_collapse", "get is the default."),
        types: z.array(z.string()).optional().describe("error|warning|log|assert|exception|all."),
        count: z.number().int().min(1).max(1000).optional().describe("Max entries, newest kept (default 50)."),
        filterText: z.string().optional(),
        format: z.enum(["plain", "detailed", "json"]).optional(),
        collapse: z.boolean().optional().describe("Merge identical messages (default true)."),
        includeStacktrace: z.boolean().optional(),
        scope: z.enum(["all", "errors_only"]).optional().describe("clear: Unity can only clear everything.")
      }
    },
    guarded(async (args) => okResult(await client.callRead("read_console", args as Record<string, unknown>, 30_000)))
  );

  server.registerTool(
    "execute_menu_item",
    {
      title: "Editor menu",
      description:
        "Run an editor menu command by path (for example File/Save Project), or list available menu paths. Use it as an escape hatch for editor features not covered by a dedicated tool; note that menus are not inspected by the Play Mode write gate.",
      inputSchema: {
        action: actionArg("execute, get_available_menus", "execute is the default."),
        menu_path: z.string().optional().describe("Full menu path, e.g. File/Save Project."),
        filter: z.string().optional().describe("Substring filter when listing."),
        limit: z.number().int().positive().optional()
      }
    },
    guarded(async (args) => okResult(await client.call("execute_menu_item", args as Record<string, unknown>, 60_000)))
  );

  server.registerTool(
    "manage_package",
    {
      title: "UPM packages",
      description:
        "List installed UPM packages, install by id/name/git URL, or remove by name. Install/remove wait for completion and survive the domain reload they can trigger.",
      inputSchema: {
        action: actionArg("list_packages, install_package, remove_package", "list_packages is the safe default."),
        id_or_url: z.string().optional().describe("Package id/name or git URL."),
        version: z.string().optional(),
        package_name: z.string().optional().describe("Package name for removal.")
      }
    },
    guarded(async (args) => {
      const action = args.action ?? "list_packages";
      const response = await client.call("manage_package", args as Record<string, unknown>, 30_000);
      if (action === "install_package" || action === "remove_package") {
        return okResult(await finishOperation(client, response, { console: "errors", timeoutMs: 300_000 }));
      }
      return okResult(response);
    })
  );

  server.registerTool(
    "manage_gameview",
    {
      title: "Game View resolution",
      description:
        "Read the Game View resolution, list available sizes, or select/create a fixed resolution. Useful before capturing the Game View or when UI layout depends on resolution.",
      inputSchema: {
        action: actionArg("get_resolution, list_resolutions, set_resolution", "get_resolution is the default."),
        width: z.number().int().min(1).max(8192).optional(),
        height: z.number().int().min(1).max(8192).optional()
      }
    },
    guarded(async (args) => okResult(await client.callRead("manage_gameview", args as Record<string, unknown>, 30_000)))
  );

  server.registerTool(
    "manage_guid",
    {
      title: "Asset GUID",
      description:
        "Resolve the authoritative 32-character GUID used by scene/prefab YAML. Use it before editing YAML or when a reference looks broken; Tuanjie encrypts the guid stored in .meta files.",
      inputSchema: {
        action: actionArg("path_to_guid", "Only path_to_guid is supported."),
        asset_path: z.string().describe("Path under Assets/ or Packages/."),
        guid: z.string().optional().describe("Expected guid to verify.")
      }
    },
    guarded(async (args) => okResult(await client.callRead("manage_guid", args as Record<string, unknown>, 30_000)))
  );

  server.registerTool(
    "manage_screenshot",
    {
      title: "Screenshots",
      description:
        "Capture the Scene view or a camera to PNG and return the image plus its file path. Prefer structured runtime data for behavior checks; use screenshots for visual verification.",
      inputSchema: {
        action: actionArg(
          "capture_scene_view, capture_scene_camera, capture_main_camera, capture_specific_camera",
          "GIF/MP4 and asset/UIToolkit capture are not implemented in this build."
        ),
        camera_name: z.string().optional().describe("capture_specific_camera: camera GameObject name."),
        width: z.number().int().min(16).max(4096).optional(),
        height: z.number().int().min(16).max(4096).optional(),
        orthographic: z.boolean().optional(),
        filename: z.string().optional(),
        include_base64: z.boolean().optional().describe("Return the image inline (default true).")
      }
    },
    guarded(async (args) => okResult(await client.call("manage_screenshot", args as Record<string, unknown>, 120_000)))
  );

  server.registerTool(
    "exec_editor_script",
    {
      title: "Run editor C#",
      description:
        "Compile and run a synchronous C# snippet inside the editor, with captured logs. Powerful escape hatch for work no dedicated tool covers; prefer dedicated tools first because they are safer and self-documenting. The snippet body is wrapped in 'public static object Run()'; use 'return value;' to return data. Blocked in Play Mode.",
      inputSchema: {
        action: z.string().optional().describe("Optional; 'run' is the only action."),
        script: z.string().optional().describe("C# statements (one of script/script_path)."),
        script_path: z.string().optional().describe("Path to a .cs file."),
        imports: z.array(z.string()).optional().describe("Extra using directives."),
        summary: z.string().optional().describe("Short purpose label."),
        timeoutSeconds: z.number().int().min(1).max(600).optional(),
        capture_logs: z.boolean().optional()
      }
    },
    guarded(async (args) => okResult(await client.call("exec_editor_script", args as Record<string, unknown>, 180_000)))
  );

  server.registerTool(
    "project_map",
    {
      title: "Project map",
      description:
        "Semantic map for understanding a project you have never seen: folder roles, scenes and what each is for, every custom component with its serialized fields and where it is already used, namespaces and entry points. Use this INSTEAD of browsing the source tree. project_overview tells you the shape; project_map tells you what is inside.",
      inputSchema: {
        force: z.boolean().optional().describe("Bypass the 30s cache."),
        maxComponents: z.number().int().min(1).max(200).optional().describe("Custom components to describe (default 40)."),
        includeFolders: z.boolean().optional(),
        includePrefabUsage: z.boolean().optional().describe("Also count component usage in prefabs (slower).")
      }
    },
    guarded(async (args) => okResult(await client.callRead("project_map", args as Record<string, unknown>, 120_000)))
  );

  server.registerTool(
    "inspect_script",
    {
      title: "Inspect a script",
      description:
        "Describe one C# script without reading it: declared type, base class, namespace, summary comment, serialized fields with types, public methods, Unity lifecycle methods, and the scene objects/prefabs that attach it. Use it after project_map or a console error names a script.",
      inputSchema: {
        path: z.string().optional().describe("Script asset path, e.g. Assets/Scripts/Foo.cs."),
        type: z.string().optional().describe("Class name, e.g. GlitchRamp."),
        includeUsage: z.boolean().optional().describe("Also list attachment locations (default true)."),
        maxPrefabs: z.number().int().min(0).max(500).optional()
      }
    },
    guarded(async (args) => okResult(await client.callRead("inspect_script", args as Record<string, unknown>, 120_000)))
  );

  server.registerTool(
    "find_usage",
    {
      title: "Find usage",
      description:
        "Find what statically references an asset, or where a component type is attached. Uses AssetDatabase dependency data for scenes/prefabs and loaded scenes for objects. Dynamic loading (Resources.Load, Addressables) is invisible, and the response says so.",
      inputSchema: {
        path: z.string().optional().describe("Asset or .cs path."),
        guid: z.string().optional().describe("Asset guid instead of a path."),
        type: z.string().optional().describe("Component class name.")
      }
    },
    guarded(async (args) => okResult(await client.callRead("find_usage", args as Record<string, unknown>, 120_000)))
  );

  server.registerTool(
    "tool_help",
    {
      title: "Tool help and examples",
      description:
        "Per-action help: exact parameters, a copy-pasteable example, the response shape, and the errors that action commonly returns. Call it before using an action for the first time or after an unexpected error. Omit `tool` to get an index of every tool.",
      inputSchema: {
        tool: z.string().optional().describe("Tool type, e.g. manage_gameobject. Omit for the tool index."),
        action: z.string().optional().describe("Action name, e.g. set_component_property. Omit for the action index.")
      }
    },
    guarded(async (args) => okResult(await client.callRead("tool_help", args as Record<string, unknown>, 60_000)))
  );
}
