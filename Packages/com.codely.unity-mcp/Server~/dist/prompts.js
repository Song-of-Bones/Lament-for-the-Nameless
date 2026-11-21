const QUICKSTART = `You are connected to a Unity/Tuanjie editor through the Codely Unity MCP bridge.

Do this in order before touching anything:

1. Call project_overview. It tells you the engine version, render pipeline, active scene, build scenes,
   package set, asset counts, Play Mode state and console health.
2. Call list_tools (or read resource unity://project/tools). It is the authoritative catalog of actions and
   parameters. Do not guess an action name; use the catalog.
3. Call manage_editor.get_state to confirm play/compile state.
4. Use manage_scene.get_hierarchy (or manage_gameobject.find / manage_asset.search) to locate the target.
5. Act with a dedicated tool. Prefer dedicated tools over exec_editor_script.
6. Verify: read_console for errors, get_state for editor state, manage_screenshot only for visual checks.

Rules that prevent most failures:
- Paths are project-relative: Assets/Scenes/Main.unity.
- Scene-object targets accept a name, a hierarchy path (Root/Child), or an instance id string.
- play, stop, refresh, request_compile and package changes wait for completion and survive domain reloads.
  If a connection drops, retry; the bridge reconnects.
- Project-mutating calls are blocked in Play Mode with code "blocked_in_play_mode". Call manage_editor.stop first.
- After editing scripts or assets, call manage_editor.request_compile (or refresh), then read_console.
- If a call fails, read its "code" and "suggestion" fields before retrying.`;
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
export function registerPrompts(server) {
    server.registerPrompt("unity-quickstart", {
        title: "Get oriented in this Unity project",
        description: "A safe, ordered workflow for an agent that has never seen this Unity project: discover the project, discover the tools, inspect, act, verify."
    }, async () => ({
        messages: [
            {
                role: "user",
                content: { type: "text", text: QUICKSTART }
            }
        ]
    }));
    server.registerPrompt("unity-safe-edit", {
        title: "Safely edit the Unity project",
        description: "A pre-flight checklist to run before mutating scenes, objects, assets or code, plus the verification steps after."
    }, async () => ({
        messages: [
            {
                role: "user",
                content: { type: "text", text: SAFE_EDIT }
            }
        ]
    }));
}
