using System;
using System.Collections.Generic;

namespace Codely.UnityMcp
{
    /// <summary>
    /// Machine-readable capability catalog. This is the first thing an agent should read:
    /// it explains what the bridge can do, when to use each tool, the exact parameter
    /// shape, and the safe call order, so an AI with no Unity knowledge can act without
    /// guessing.
    /// </summary>
    public static class ToolCatalog
    {
        private static Dictionary<string, object> P(string name, string type, bool required, string description,
            string defaultValue = null, string note = null)
        {
            var p = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["name"] = name,
                ["type"] = type,
                ["required"] = required,
                ["description"] = description
            };
            if (defaultValue != null) p["default"] = defaultValue;
            if (note != null) p["note"] = note;
            return p;
        }

        private static Dictionary<string, object> A(string description, params object[] parameters)
        {
            var action = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["description"] = description
            };
            if (parameters != null && parameters.Length > 0)
                action["params"] = new List<object>(parameters);
            return action;
        }

        private static Dictionary<string, object> Tool(string type, string summary, string whenToUse,
            Dictionary<string, object> actions, string[] notes = null, string[] examples = null)
        {
            var tool = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["type"] = type,
                ["summary"] = summary,
                ["when_to_use"] = whenToUse,
                ["actions"] = actions
            };
            if (notes != null && notes.Length > 0) tool["notes"] = new List<object>(notes);
            if (examples != null && examples.Length > 0) tool["examples"] = new List<object>(examples);
            return tool;
        }

        public static ToolResult Describe()
        {
            var tools = new List<object>
            {
                Tool("project_map",
                    "Semantic map of the project: folders and their roles, scenes and what they are for, custom components with their serialized fields, where each component is already used, namespaces and entry points.",
                    "Call this instead of browsing the source tree when you need to know what exists and which script owns a feature. project_overview tells you the shape of the project; project_map tells you what is inside it.",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["describe"] = A("Return the map. Cached for 30 seconds unless force=true.",
                            P("force", "bool", false, "Bypass the cache.", "false"),
                            P("maxComponents", "int", false, "Maximum custom components to describe.", "40"),
                            P("includeFolders", "bool", false, "Include folder roles and file-kind counts.", "true"),
                            P("includePrefabUsage", "bool", false, "Also count component usage inside prefabs (slower).", "false")),
                    },
                    new[]
                    {
                        "components[].type is the exact name for manage_gameobject.get_components.",
                        "components[].fields[].name is the exact serialized field for set_component_property.",
                        "Only loaded scenes contribute used_in_scene entries."
                    },
                    new[] { "{\"type\":\"project_map\",\"params\":{}}" }),

                Tool("tool_help",
                    "Per-tool/per-action help: exact parameters, a copy-pasteable example, the response shape, and the errors that action commonly returns.",
                    "Call this before using an action for the first time, or after an unexpected error. It is the fastest way to learn a tool without reading source.",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["describe"] = A("Return help for one action, or the action index for a tool.",
                            P("tool", "string", true, "Tool type, e.g. manage_gameobject."),
                            P("action", "string", false, "Action name, e.g. set_component_property. Omit for the action index.")),
                    },
                    new[]
                    {
                        "Omit tool to get an index of every tool.",
                        "The example object can be sent as-is, then edited."
                    },
                    new[]
                    {
                        "{\"type\":\"tool_help\",\"params\":{\"tool\":\"manage_gameobject\",\"action\":\"set_component_property\"}}"
                    }),

                Tool("inspect_script",
                    "Describe one C# script: declared type, base class, namespace, summary comment, serialized fields with types, public methods, Unity lifecycle methods, and where the component is attached.",
                    "Use after project_map names a component, or when a console error mentions a script. It replaces opening the .cs file.",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["describe"] = A("Inspect by asset path or by class name.",
                            P("path", "path", false, "Script asset path, e.g. Assets/动画/脚本/雪花效果控制器.cs."),
                            P("type", "string", false, "Class name, e.g. GlitchRamp."),
                            P("includeUsage", "bool", false, "Also list scene objects and prefabs using it.", "true"),
                            P("maxPrefabs", "int", false, "Prefabs to scan for usage.", "150")),
                    },
                    new[]
                    {
                        "fields[].name is what set_component_property expects.",
                        "lifecycle lists which Unity messages the script implements."
                    },
                    new[] { "{\"type\":\"inspect_script\",\"params\":{\"type\":\"GlitchRamp\"}}" }),

                Tool("find_usage",
                    "Find what references an asset, or where a component type is attached, using AssetDatabase dependency data and loaded scenes.",
                    "Use before deleting/renaming an asset, or when you need an instance of a component to modify.",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["describe"] = A("Resolve references for an asset path, guid, or component type.",
                            P("path", "path", false, "Asset or .cs path."),
                            P("guid", "string", false, "Asset guid instead of path."),
                            P("type", "string", false, "Component class name.")),
                    },
                    new[]
                    {
                        "Dynamic loading (Resources.Load, Addressables, string paths) is invisible; a zero is not proof of no usage.",
                        "Static scan is capped at 300 prefabs per call."
                    },
                    new[] { "{\"type\":\"find_usage\",\"params\":{\"path\":\"Assets/美术素材/圆角按钮.png\"}}" }),

                Tool("project_overview",
                    "One-shot description of the Unity project: version, render pipeline, active scene, build scenes, packages, script/asset counts, play state and console health.",
                    "Call this FIRST in any new session, or whenever the user asks 'what is this project / how do I use it'. It replaces guessing from file names.",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["describe"] = A("Return the full project briefing. No parameters."),
                    },
                    new[]
                    {
                        "Output is intentionally compact but complete enough to choose the next tool.",
                        "Combine with list_tools: project_overview explains the project, list_tools explains the bridge."
                    },
                    new[] { "{\"type\":\"project_overview\",\"params\":{}}" }),

                Tool("list_tools",
                    "Authoritative catalog of every bridge tool, its actions, parameters and safety notes.",
                    "Call this when unsure which tool/action to use, or before a complex multi-step task.",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["describe"] = A("Return this catalog. No parameters."),
                    }),

                Tool("manage_editor",
                    "Editor state and lifecycle: read state, enter/leave Play Mode, refresh assets, compile scripts, wait for idle, tags/layers, windows, selection, active tool.",
                    "Use for anything about the editor itself rather than a scene object or asset. Start with get_state.",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["get_state"] = A("Full snapshot: play mode, compiling/updating, active scene, selection, console counts, render pipeline, project root."),
                        ["play"] = A("Enter Play Mode. Returns immediately with status 'started'; poll get_operation until done.",
                            P("timeoutSeconds", "int", false, "Client-side budget hint.", "120")),
                        ["stop"] = A("Leave Play Mode (discards runtime state). Returns 'started'; poll get_operation.",
                            P("timeoutSeconds", "int", false, "Client-side budget hint.", "120")),
                        ["pause"] = A("Pause or unpause Play Mode.",
                            P("targetPaused", "bool", false, "Desired state; omit to toggle.")),
                        ["resume"] = A("Resume Play Mode, or advance exactly one frame.",
                            P("singleFrame", "bool", false, "true = step one frame, false = resume.", "false")),
                        ["step"] = A("Advance one frame while paused."),
                        ["get_operation"] = A("Poll the last long-running operation (play/stop/refresh/compile). Returns state: idle|running|done|failed."),
                        ["refresh"] = A("AssetDatabase.Refresh + wait for import. Returns 'started'; poll get_operation. Blocked in Play Mode.",
                            P("console_mode", "enum", false, "none|errors|full — console embedded in the result.", "errors"),
                            P("include_console", "bool", false, "Legacy alias: true => full, false => none."),
                            P("timeoutSeconds", "int", false, "Client-side budget hint.", "300")),
                        ["request_compile"] = A("Force script compilation and return diagnostics. Returns 'started'; poll get_operation.",
                            P("clearCache", "bool", false, "Recompile everything.", "false"),
                            P("console_mode", "enum", false, "none|errors|full.", "errors")),
                        ["get_compilation_summary"] = A("Error/warning counts from captured console output since the last clear."),
                        ["wait_for_idle"] = A("Check whether the editor is idle (not compiling/updating). Poll until idle=true."),
                        ["get_tags"] = A("List project tags."),
                        ["ensure_tag"] = A("Create a tag if missing (idempotent).", P("tagName", "string", true, "Tag name.")),
                        ["add_tag"] = A("Create a tag; fails if it already exists.", P("tagName", "string", true, "Tag name.")),
                        ["remove_tag"] = A("Delete a tag.", P("tagName", "string", true, "Tag name.")),
                        ["get_layers"] = A("List named layers with their indices."),
                        ["ensure_layer"] = A("Create a layer if missing (idempotent).", P("layerName", "string", true, "Layer name.")),
                        ["add_layer"] = A("Create a layer; fails if it already exists.", P("layerName", "string", true, "Layer name.")),
                        ["remove_layer"] = A("Delete a layer by name.", P("layerName", "string", true, "Layer name.")),
                        ["get_windows"] = A("List open editor windows (type, title, focused)."),
                        ["focus_window"] = A("Focus an editor window by type name, e.g. SceneView, GameView, InspectorWindow.",
                            P("windowType", "string", true, "Window type name.")),
                        ["get_selection"] = A("Current editor selection (scene objects and assets)."),
                        ["get_active_tool"] = A("Current editor tool (Move/Rotate/Scale/View/Transform)."),
                        ["set_active_tool"] = A("Set the editor tool.", P("toolName", "string", true, "Move|Rotate|Scale|View|Transform.")),
                        ["get_project_root"] = A("Absolute project root path."),
                    },
                    new[]
                    {
                        "play/stop/refresh/request_compile are asynchronous by design: the editor may reload its domain, which drops TCP connections. They return 'started' and you poll get_operation.",
                        "In Play Mode, project-mutating commands return code 'blocked_in_play_mode'; call stop first."
                    },
                    new[]
                    {
                        "{\"type\":\"manage_editor\",\"params\":{\"action\":\"get_state\"}}",
                        "{\"type\":\"manage_editor\",\"params\":{\"action\":\"play\"}} then poll {\"action\":\"get_operation\"}"
                    }),

                Tool("manage_scene",
                    "Scene files: open, save, create, and read the loaded hierarchy.",
                    "Use before inspecting or editing objects: the hierarchy only contains loaded scenes.",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["load"] = A("Open a scene by name, path or build index (single mode).",
                            P("name", "string", false, "Scene name, extension optional."),
                            P("path", "path", false, "Directory under Assets/ or an explicit .unity path."),
                            P("buildIndex", "int", false, "Build-settings index instead of name/path.", null, "alias: build_index")),
                        ["save"] = A("Save the active scene, or save-as when name/path is given.",
                            P("name", "string", false, "New scene name for save-as."),
                            P("path", "path", false, "Target directory under Assets/.")),
                        ["create"] = A("Create a new empty scene and save it.",
                            P("name", "string", true, "Scene name."),
                            P("path", "path", false, "Directory under Assets/.", "Scenes")),
                        ["get_hierarchy"] = A("Hierarchy of all loaded scenes (depth/node limited for large scenes).",
                            P("maxDepth", "int", false, "Maximum child depth.", "6"),
                            P("maxNodes", "int", false, "Maximum returned nodes.", "2000")),
                    },
                    new[] { "Scene paths are relative to the project, e.g. Assets/Scenes/Main.unity." }),

                Tool("manage_gameobject",
                    "Scene object operations: find, create, modify, delete, components, children.",
                    "Use after manage_scene.get_hierarchy or find to obtain a target. Writes are blocked in Play Mode.",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["find"] = A("Find objects by name, hierarchy path or instance id, or fuzzy searchTerm.",
                            P("target", "string", false, "Name, path or instanceID. One of target/searchTerm is required."),
                            P("searchTerm", "string", false, "Case-insensitive contains filter; overrides target."),
                            P("searchMethod", "enum", false, "by_name|by_path|by_id."),
                            P("searchInactive", "bool", false, "Include inactive objects.", "true"),
                            P("findAll", "bool", false, "Return every match instead of the first.", "false")),
                        ["create"] = A("Create an empty object or primitive, optionally parented and with components.",
                            P("name", "string", true, "Object name."),
                            P("primitiveType", "enum", false, "cube|sphere|capsule|cylinder|plane|quad."),
                            P("parent", "string", false, "Parent name/path/instanceID."),
                            P("position", "vec3", false, "[x,y,z] local position."),
                            P("rotation", "vec3", false, "Euler degrees."),
                            P("scale", "vec3", false, "[x,y,z]."),
                            P("tag", "string", false, "Tag name."),
                            P("layer", "int", false, "Layer index."),
                            P("componentsToAdd", "json", false, "Array of {type, properties}."),
                            P("saveAsPrefab", "bool", false, "Save the result as a prefab.", "false"),
                            P("prefabPath", "path", false, "Prefab path ending with .prefab.")),
                        ["modify"] = A("Change transform, identity, parent, active state or components. Blocked in Play Mode.",
                            P("target", "string", true, "Name, path or instanceID."),
                            P("searchMethod", "enum", false, "by_name|by_path|by_id."),
                            P("name", "string", false, "New name."),
                            P("tag", "string", false, "New tag."),
                            P("layer", "int", false, "New layer index."),
                            P("setActive", "bool", false, "Active state."),
                            P("parent", "string", false, "New parent (empty string = root)."),
                            P("position", "vec3", false, "Local position."),
                            P("rotation", "vec3", false, "Euler degrees."),
                            P("scale", "vec3", false, "Local scale."),
                            P("componentsToAdd", "json", false, "Array of {type, properties}."),
                            P("componentsToRemove", "json", false, "Array of type names."),
                            P("componentProperties", "json", false, "Object: component type => {property: value}.")),
                        ["delete"] = A("Delete an object (DestroyImmediate in edit mode).",
                            P("target", "string", true, "Name, path or instanceID."),
                            P("searchMethod", "enum", false, "by_name|by_path|by_id.")),
                        ["get_components"] = A("List components and their serialized properties.",
                            P("target", "string", true, "Name, path or instanceID."),
                            P("searchMethod", "enum", false, "by_name|by_path|by_id."),
                            P("includeNonPublicSerialized", "bool", false, "Include hidden serialized fields.", "true")),
                        ["remove_component"] = A("Remove one component by type name.",
                            P("target", "string", true, "Object."),
                            P("componentName", "string", true, "Component type name.")),
                        ["set_component_property"] = A("Set one or more properties on a component.",
                            P("target", "string", true, "Object."),
                            P("componentName", "string", true, "Component type name."),
                            P("propertyName", "string", false, "Single property name."),
                            P("propertyValue", "json", false, "Value for propertyName."),
                            P("componentProperties", "json", false, "Alternative: {property: value, ...}.")),
                        ["list_children"] = A("List direct children.",
                            P("target", "string", true, "Object.")),
                        ["ensure_component"] = A("Add a component only if absent (idempotent).",
                            P("target", "string", true, "Object."),
                            P("component_type", "string", true, "Component type name.", null, "alias: componentType")),
                        ["ensure_renderer_material"] = A("Assign a material to a renderer if the slot is empty.",
                            P("target", "string", true, "Object."),
                            P("material", "string", true, "Material name or asset path.")),
                        ["ensure_mesh_collider_mesh"] = A("Point a MeshCollider at its MeshFilter mesh.",
                            P("target", "string", true, "Object."),
                            P("mesh", "string", false, "Optional mesh override.")),
                        ["ensure_prefab_default_sprite"] = A("Ensure a prefab has a default sprite assigned.",
                            P("prefab", "path", true, "Prefab asset path."),
                            P("sprite", "path", false, "Optional sprite asset path.")),
                    },
                    new[]
                    {
                        "target accepts: exact name, hierarchy path (Root/Child/Grandchild), or instance id as string.",
                        "findAll=false returns the first match; use findAll=true when duplicating names may exist."
                    },
                    new[] { "{\"type\":\"manage_gameobject\",\"params\":{\"action\":\"find\",\"target\":\"Main Camera\"}}" }),

                Tool("manage_asset",
                    "Asset database: search, inspect, create and modify assets, and create prefab variants.",
                    "Use to find art/prefabs/materials and to inspect import metadata. Asset paths are relative to the project root.",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["search"] = A("Search assets by name pattern, folder and type.",
                            P("searchPattern", "string", false, "Name substring, e.g. 'hero'."),
                            P("path", "path", false, "Folder under Assets/ to restrict to.", null, "default: Assets"),
                            P("filterType", "string", false, "Type name, e.g. Material, Prefab, Texture2D, ScriptableObject."),
                            P("filterDateAfter", "string", false, "ISO date; only assets modified after."),
                            P("pageSize", "int", false, "Page size (1-200).", "50"),
                            P("pageNumber", "int", false, "1-based page.", "1"),
                            P("generatePreview", "bool", false, "Include preview availability.", "false")),
                        ["get_info"] = A("Metadata for one asset: type, guid, size, dependencies.",
                            P("path", "path", true, "Asset path."),
                            P("generatePreview", "bool", false, "Include a preview image path when available.", "false")),
                        ["create"] = A("Create a Material, ScriptableObject or other asset type.",
                            P("path", "path", true, "Target asset path with extension."),
                            P("assetType", "string", true, "Type name, e.g. Material, or a ScriptableObject type."),
                            P("properties", "json", false, "Initial property values.")),
                        ["modify"] = A("Set serialized properties on an asset (materials accept shader property names).",
                            P("path", "path", true, "Asset path."),
                            P("properties", "json", true, "Property values; a no-op request fails.")),
                        ["ensure_prefab_variant"] = A("Create a prefab variant from a source prefab if missing.",
                            P("path", "path", true, "Variant path ending with .prefab."),
                            P("sourcePrefab", "path", true, "Source prefab path.")),
                    },
                    new[] { "searchPattern is a substring of the file name, not a regex." }),

                Tool("read_console",
                    "Read or clear Unity console messages, with severity filters and optional grouping.",
                    "Call after refresh/compile/play, or when something looks wrong. Errors usually explain the next fix.",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["get"] = A("Read captured console entries.",
                            P("types", "json", false, "Array: error|warning|log|assert|exception|all.", "[\"error\",\"warning\",\"log\"]"),
                            P("count", "int", false, "Max entries (1-1000), newest kept.", "50"),
                            P("filterText", "string", false, "Case-insensitive substring filter."),
                            P("format", "enum", false, "plain|detailed|json.", "plain"),
                            P("collapse", "bool", false, "Merge identical messages with [xN].", "true"),
                            P("includeStacktrace", "bool", false, "Include stack traces.", "false")),
                        ["clear"] = A("Clear captured entries.",
                            P("scope", "enum", false, "all|errors_only (Unity cannot clear only errors).", "all")),
                        ["set_collapse"] = A("Persist a grouping preference.",
                            P("collapse", "bool", true, "Preferred grouping.")),
                    },
                    new[] { "The bridge only sees messages logged while the editor is running with the package loaded." }),

                Tool("execute_menu_item",
                    "Run an editor menu command by path (File/Save Project, Assets/Refresh, custom tools).",
                    "Use as an escape hatch for editor functionality not covered by a dedicated tool.",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["execute"] = A("Execute a menu item path. Default action.",
                            P("menu_path", "string", true, "Full menu path, e.g. File/Save Project.", null, "alias: menuPath")),
                        ["get_available_menus"] = A("List known menu paths (best effort reflection).",
                            P("menu_path", "string", false, "Root to list from.", null, "alias: menuPath"),
                            P("filter", "string", false, "Substring filter.")),
                    },
                    new[] { "Menu items can change the project; the Play Mode write gate does not inspect them." }),

                Tool("manage_package",
                    "UPM packages: list installed packages, install by id/url, remove by name.",
                    "Use when the project is missing a dependency or the user asks which packages are installed.",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["list_packages"] = A("List installed packages with name, version, source."),
                        ["install_package"] = A("Install a package (returns 'started' when a reload is likely; poll get_operation).",
                            P("id_or_url", "string", true, "Package id/name or git URL."),
                            P("version", "string", false, "Optional version pin.")),
                        ["remove_package"] = A("Remove a package by name.", P("package_name", "string", true, "Package name.")),
                    },
                    new[] { "Install/remove can trigger a domain reload; the MCP server reconnects automatically." }),

                Tool("manage_gameview",
                    "Game View resolution: read the current size, list available sizes, set a fixed size.",
                    "Use before capturing the Game View or when UI layout depends on resolution.",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["get_resolution"] = A("Current Game View resolution."),
                        ["list_resolutions"] = A("All resolutions in the current size group."),
                        ["set_resolution"] = A("Select or create a fixed resolution (1-8192).",
                            P("width", "int", true, "Width in pixels."),
                            P("height", "int", true, "Height in pixels.")),
                    }),

                Tool("manage_guid",
                    "Resolve the authoritative asset GUID used by scene/prefab YAML.",
                    "Use before editing YAML or when a reference looks broken. Tuanjie encrypts the guid in .meta files.",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["path_to_guid"] = A("Return the guid for an asset path, optionally verifying an expected value.",
                            P("asset_path", "path", true, "Path under Assets/ or Packages/."),
                            P("guid", "string", false, "Expected guid to verify.")),
                    }),

                Tool("manage_screenshot",
                    "Capture the Scene view or a camera to PNG and return the image plus its file path.",
                    "Use for visual verification. Prefer runtime data (get_state, logs) over screenshots for behavior checks.",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["capture_scene_view"] = A("Render the active Scene view camera.",
                            P("width", "int", false, "Output width.", "960"),
                            P("height", "int", false, "Output height.", "540"),
                            P("view", "enum", false, "current|front|back|left|right|top|bottom|iso.", "current"),
                            P("orthographic", "bool", false, "Use orthographic projection.", "false"),
                            P("filename", "string", false, "Output file name (PNG added).")),
                        ["capture_main_camera"] = A("Render Camera.main.",
                            P("width", "int", false, "Output width."),
                            P("height", "int", false, "Output height."),
                            P("filename", "string", false, "Output file name.")),
                        ["capture_specific_camera"] = A("Render a named camera.",
                            P("camera_name", "string", true, "Camera GameObject name."),
                            P("width", "int", false, "Output width."),
                            P("height", "int", false, "Output height."),
                            P("filename", "string", false, "Output file name.")),
                    },
                    new[]
                    {
                        "PNG is written under <project>/screenshots/ and also returned as base64 in data.image_base64 for clients that render images.",
                        "GIF and MP4 are not implemented in this build; the action returns code 'not_supported' with guidance."
                    }),

                Tool("exec_editor_script",
                    "Compile and run a C# snippet in the editor process (synchronous only), with optional captured logs.",
                    "Powerful escape hatch when no dedicated tool covers the task. Prefer dedicated tools first: they are safer and self-documenting.",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["run"] = A("Compile the snippet and invoke static object Run().",
                            P("script", "string", false, "C# statements (one of script/script_path)."),
                            P("script_path", "path", false, "Path to a .cs file under the project."),
                            P("imports", "json", false, "Extra using directives."),
                            P("summary", "string", false, "Short purpose label returned in the result."),
                            P("timeoutSeconds", "int", false, "Execution budget (1-600).", "60"),
                            P("capture_logs", "bool", false, "Capture Debug.Log output while running.", "true")),
                    },
                    new[]
                    {
                        "The snippet body is wrapped in 'public static object Run()'. Use 'return value;' to return data.",
                        "Blocked in Play Mode: call manage_editor.stop first.",
                        "Task/IEnumerator returns are not awaited in this build; return a plain value."
                    },
                    new[]
                    {
                        "{\"type\":\"exec_editor_script\",\"params\":{\"script\":\"return Application.dataPath;\",\"summary\":\"where is the project\"}}"
                    })
            };

            var data = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["bridge"] = "codely-unity-mcp",
                ["package_version"] = McpBridgeServer.PackageVersion,
                ["protocol"] = McpBridgeServer.ProtocolVersion,
                ["unity_version"] = UnityEngine.Application.unityVersion,
                ["tool_count"] = tools.Count,
                ["tools"] = tools,
                ["recommended_flow"] = new List<object>
                {
                    "1. project_overview — learn what the project is",
                    "2. list_tools — learn what the bridge can do",
                    "3. manage_editor.get_state — confirm play mode and compile state",
                    "4. manage_scene.get_hierarchy or manage_asset.search — locate the target",
                    "5. act with a dedicated tool; verify with read_console / get_state / screenshot",
                    "6. after code edits: manage_editor.refresh or request_compile, then read_console"
                },
                ["conventions"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["paths"] = "Asset paths are project-relative and start with Assets/ (e.g. Assets/Scenes/Main.unity).",
                    ["targets"] = "Scene-object targets accept a name, a hierarchy path (Root/Child), or an instance id string.",
                    ["async_ops"] = "play/stop/refresh/request_compile/package changes return 'started'. Poll manage_editor.get_operation until state is done.",
                    ["play_mode"] = "Project-mutating calls return code 'blocked_in_play_mode'. Stop Play Mode first.",
                    ["errors"] = "Failures return success=false with a stable 'code' and an actionable message; read it before retrying."
                }
            };

            return new ToolResult($"Listed {tools.Count} tool types.", data);
        }
    }
}
