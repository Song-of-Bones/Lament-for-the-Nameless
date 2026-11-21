# Codely Unity MCP

A self-contained Unity/Tuanjie editor bridge plus an MCP server, so any MCP-capable AI
client (Claude Code, Cursor, Codex, Claude Desktop, …) can inspect and operate the
project without Tuanjie Cowork.

No native plugins. The editor side is plain C#; the client side is a Node/TypeScript
stdio server.

---

## What you get

- **13 tools** covering editor state and Play Mode, scenes, GameObjects, assets, console,
  menus, UPM packages, Game View resolution, GUIDs, PNG screenshots and synchronous C#.
- **Project overview first**: one call returns engine version, render pipeline, active
  scene, build scenes, packages, asset counts and console health — enough for an AI that
  has never seen the project to choose the next step.
- **Self-describing catalog**: `list_tools` returns every action, parameter, safety rule
  and the recommended call order.
- **MCP resources**: `unity://project/overview`, `unity://project/tools`,
  `unity://project/hierarchy`, `unity://project/console`.
- **MCP prompts**: `unity-quickstart`, `unity-safe-edit`.
- **Survives domain reloads**: play/stop/refresh/compile/package changes return only after
  they finish, reconnecting through the reload automatically.

## Requirements

- Unity/Tuanjie 2021.3 or newer (tested on Tuanjie 6000.6.0f1).
- Node.js 18+ for the MCP server.

## Layout

```
Packages/com.codely.unity-mcp/
├── Editor/                 Unity editor half (compiled by Unity)
│   ├── Core/               transport, framing, JSON, console, write guard, paths
│   ├── Tools/              one file per tool
│   └── UI/                 Tools > Codely > Unity MCP status window
├── Server~/                MCP server (ignored by Unity because of the ~ suffix)
│   ├── src/                TypeScript source
│   ├── dist/               built output
│   └── node_modules/       installed dependencies
└── README.md
```

The package is embedded (`Packages/`), so Unity loads it automatically. The editor bridge
listens on `127.0.0.1` and writes its port to `Temp/.codely-unity-mcp.json`.

## Setup

1. Open the project. The bridge starts automatically; confirm with
   **Tools > Codely > Unity MCP** (shows port, clients, command count).
2. Build the MCP server once:

   ```powershell
   cd "Packages/com.codely.unity-mcp/Server~"
   npm install
   npm run build
   ```

3. Register the server with your MCP client.

   **Claude Code**

   ```powershell
   claude mcp add unity -- node "K:/path/to/project/Packages/com.codely.unity-mcp/Server~/dist/index.js"
   ```

   **Cursor / Claude Desktop / other JSON config**

   ```json
   {
     "mcpServers": {
       "unity": {
         "command": "node",
         "args": ["K:/path/to/project/Packages/com.codely.unity-mcp/Server~/dist/index.js"],
         "env": {
           "UNITY_MCP_PROJECT": "K:/path/to/project"
         }
       }
     }
   }
   ```

   `UNITY_MCP_PROJECT` is optional when the server lives inside the project: it infers the
   root from its own location, then from the working directory.

## First run

Ask the client to:

1. call `project_overview`;
2. call `list_tools`;
3. call `manage_editor.get_state`.

That sequence gives the model the project, the capability surface and the current editor
mode before it touches anything.

## Tool surface

| Tool | Purpose |
|---|---|
| `project_overview` | One-shot project briefing |
| `list_tools` | Authoritative catalog with parameters and safety notes |
| `manage_editor` | State, Play Mode, refresh, compile, idle, tags/layers, windows, selection |
| `manage_scene` | Load/save/create scenes, read hierarchy |
| `manage_gameobject` | Find/create/modify/delete objects, components, children |
| `manage_asset` | Search/inspect/create/modify assets, prefab variants |
| `read_console` | Read/clear console with filters and grouping |
| `execute_menu_item` | Run editor menu items, list known paths |
| `manage_package` | List/install/remove UPM packages |
| `manage_gameview` | Read/list/set Game View resolution |
| `manage_guid` | Authoritative asset GUID |
| `manage_screenshot` | PNG from Scene view or a camera (returned inline) |
| `exec_editor_script` | Compile and run synchronous C# in the editor |

## Safety model

- The bridge binds to loopback only.
- Project-mutating calls are rejected while in Play Mode with
  `code: "blocked_in_play_mode"`; the message tells the model to stop Play Mode first.
- Paths are normalized and writes must stay under `Assets/`.
- Object mutations use Unity's Undo and mark the scene dirty, so a human can undo them.
- `exec_editor_script` is the escape hatch: it runs arbitrary editor code and is therefore
  the highest-risk tool. Prefer dedicated tools.

## Limits in this build

- Screenshots are PNG only. GIF, MP4, asset-preview capture and UI Toolkit capture return
  `not_supported`.
- `exec_editor_script` is synchronous: `Task`/`IEnumerator` results are returned as objects,
  not awaited.
- Editor window streaming (`manage_window_bridge`) is not implemented; it depends on
  native window and WebRTC code.
- Native modal-dialog clicking is not implemented.
- Input simulation (`manage_input`) is not implemented.

## Troubleshooting

| Symptom | Fix |
|---|---|
| `bridge_not_found` | Open the editor, or set `UNITY_MCP_PROJECT` to the folder containing `Assets/`. |
| `bridge_not_ready` | A domain reload is in progress. Retry in a few seconds. |
| `connection_lost` | Same; the next call reconnects. Re-check state before replaying a write. |
| Port changes between sessions | Expected: the port is persisted per project but may move if occupied. The heartbeat file is authoritative. |
| Bridge stopped after a manual stop | **Tools > Codely > Unity MCP > Start**, or restart the editor. |

## Development

```powershell
cd "Packages/com.codely.unity-mcp/Server~"
npm run dev      # tsc --watch
npm start        # run the stdio server manually
```

After changing C# files, let Unity recompile (or run `Assets/Refresh`); the bridge restarts
itself after the domain reload.
