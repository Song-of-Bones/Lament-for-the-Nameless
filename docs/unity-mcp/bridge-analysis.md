# Codely Bridge 1.0.93 拆解报告

分析对象：`Library/PackageCache/cn.tuanjie.codely.bridge@fd2b9ee67c14`
（UPM 包名 `cn.tuanjie.codely.bridge`，本机版本 1.0.93）

目的：把 Tuanjie Cowork 与 Unity 之间的私有通道拆清楚，作为独立 MCP 实现的行为契约。

---

## 1. 总体结构

```
AI 客户端（Cowork 内置 Agent）
   │  Cowork 自己的协议（不在本包内）
   ▼
Codely CLI / Cowork 客户端进程
   │  TCP 127.0.0.1:<动态端口>
   ▼
NativeTcpBridge.dll / .so / .dylib   ← 真正的 TCP 服务器、帧协议、心跳写入
   │  C ABI（NTB_*，ABI=2）
   ▼
C# 层（Editor/Bridge）
   ├─ UnityTcpBridge       命令泵、dispatch switch、生命周期
   ├─ BackgroundCommandPump 后台线程（只跑 manage_job / manage_dialog）
   ├─ Tools/*              21 个 Agent 工具的实现
   ├─ Helpers/*            作业、协程、写保护、对话框、状态快照
   └─ Native/*             P/Invoke 包装
   ▼
UnityEditor API / 场景 / 资源 / 播放模式
```

关键结论：**TCP 服务器和帧协议在闭源原生库里**，C# 只负责“从原生队列取命令 → 主线程执行 → 把响应塞回原生队列”。因此不存在“只替换 C# 就能复用原协议”的路径。

另有两条独立通道：

- `Editor/Tauri/*`：Cowork 的编辑器内嵌窗口（WebView2 / WKWebView），走 `NativeWindowBridge` + `libdatachannel`（WebRTC）做窗口画面/输入流。与 Agent 工具无关，是 UI 层。
- `Editor/Common/*` + `Runtime/PlayModeCoroutineHost.cs`：主线程判定、编辑器进程守卫、播放模式协程宿主。

---

## 2. 传输层（已实测确认）

| 项目 | 值 |
|---|---|
| 监听地址 | 127.0.0.1 |
| 端口 | 默认 25916 起探测，动态分配；本机当前 **38475** |
| 握手（服务器→客户端） | `WELCOME UNITY-TCP 1 FRAMING=1 SERVER_VERSION=3` |
| 握手（客户端→服务器） | `PLATFORM=<win/mac/linux>`、`CLIENT_VERSION=<n>`；`>=2` 才接收推送通知 |
| 帧格式 | `FRAMING=1`，原生实现，未公开；单帧上限 64 MiB |
| 心跳文件 | `<ProjectRoot>/Temp/.com-unity-codely.json` |
| 发现方式 | 客户端读心跳文件拿端口，连上后读 WELCOME |

心跳文件实测内容：

```json
{
  "unity_port": 38475,
  "project_path": "K:/Game/Lament-for-the-Nameless/Assets",
  "reloading": false,
  "reason": "ready",
  "last_updated": "2026-11-20T23:04:51.826Z"
}
```

`reason` 用于区分主动停止（`manually_stopped`）、退出（`unity_quit`）、包更新（`package_updating`）、域重载中（`reloading`）。

### 消息封套

请求（客户端→Unity）：

```json
{ "type": "manage_editor", "params": { "action": "get_state" } }
```

响应（Unity→客户端）：

```json
{ "success": true, "message": "Command executed successfully", "data": { } }
{ "success": false, "message": "...", "code": "...", "error": "...", "data": { } }
```

推送（Unity→客户端，仅 CLIENT_VERSION>=2）：

```json
{ "notification_type": "scene_changed", "payload": { } }
```

原生层为每个请求分配 `request_id`（ulong），负责“命令 → 响应”配对与超时清理；C# 只看见 `requestId + commandText`。

---

## 3. 生命周期与线程模型

- `[InitializeOnLoad]` 自动启动；`SessionState` 记录“手动停止”，跨域重载保持，进程退出后遗忘。
- 域重载前：`PauseManagedCommandProcessing()` + 取消所有 Task/协程作业 + 落盘作业状态。
- 域重载后：原生监听器仍在，C# 重新 attach，恢复队列。
- 命令泵：`EditorApplication.update` 每帧最多取 128 条，主线程执行。
- 后台泵：仅 `manage_job`（线程安全作业控制）与 `manage_dialog`（点击阻塞主线程的原生对话框）在工作线程执行；其他命令即使错投后台也会被拒绝。
- 播放模式退出时：取消所有在途作业，并给每个请求回错误。
- **所有 Agent 工具都是“运行到完成”语义**：没有 `op_id` / 轮询字段，调用返回即工作完成。

---

## 4. 工具清单（21 个 Agent 可见工具）

来源：`Editor/Bridge/Tools/command-catalog.xml`（权威目录，dispatch 与它必须同步）+ `UnityTcpBridge.ExecuteCommand` 的 switch。

| # | 工具 | 别名 | 超时预算 | 作用 |
|---|---|---|---|---|
| 1 | `manage_scene` | `scene` | 30s | 场景加载/保存/新建/层级 |
| 2 | `manage_editor` | `editor` | 310s（refresh 310s，wait_for_idle 610s） | 播放模式、编译、刷新、标签/层、窗口、选择、状态 |
| 3 | `manage_gameobject` | `gameobject`,`go` | 30s | 查找/创建/修改/删除/组件/子物体 |
| 4 | `manage_asset` | — | 30s | 资源创建/修改/搜索/信息/Prefab Variant |
| 5 | `manage_guid` | — | 30s | 资源真实 GUID（Tuanjie 的 .meta 是加密 GUID） |
| 6 | `manage_shader` | — | 30s（compile 61s） | 着色器编译、预览、管线检测 |
| 7 | `read_console` | `console` | 30s | 读/清控制台、折叠模式 |
| 8 | `execute_menu_item` | — | 30s | 执行编辑器菜单、列出菜单 |
| 9 | `execute_csharp_script` | `eval` | 310s | Roslyn 执行 C#（旧命令，无模式切换） |
| 10 | `exec_editor_script` | — | 430s | 编辑模式 Roslyn 执行，可切模式 |
| 11 | `exec_runtime_script` | — | 430s | 播放模式 Roslyn 执行，自动进 Play |
| 12 | `manage_package` | — | 310s | UPM 安装/移除/列表 |
| 13 | `manage_bake` | — | 610s | 光照/NavMesh 烘焙、清理 |
| 14 | `execute_custom_tool` | — | 30s | 调用 `[CustomTool]` 注册的用户工具 |
| 15 | `get_custom_tools` | — | 30s | 列出用户工具 |
| 16 | `manage_window_bridge` | — | 30s | 编辑器窗口聚焦/原生句柄/输入/画面流 |
| 17 | `manage_gameview` | `gameview`,`gv` | 30s | Game View 分辨率查询/设置 |
| 18 | `manage_screenshot` | `screenshot` | 120s | 场景/相机/资源/UI Toolkit 截图、GIF、MP4 |
| 19 | `manage_input` | `input` | 60s（sequence 120s） | Play Mode 虚拟鼠标键盘、文本、步骤序列 |
| 20 | `manage_dialog` | `dialog` | 30s | 点击阻塞主线程的原生模态框 |
| 21 | `list_tools` | — | 30s | 返回本目录（工具/动作/参数 schema/超时） |

内部命令（不对 Agent 暴露）：`_internal_state_dirty`、`_internal_asset_listening`、`_internal_refresh_app_editor_support`、`_internal_list_app_editor_support_packages`、`get_app_editor_actions`、`execute_app_editor_action`。

### 4.1 各工具动作摘要

**manage_scene**：`load`（name/path/buildIndex）、`save`（可另存）、`create`、`get_hierarchy`（大场景只返回根）。

**manage_editor**：`play` / `stop` / `pause` / `resume`（可单帧）/ `step` / `get_state` / `get_current_state` / `refresh` / `request_compile` / `start_compilation_pipeline` / `get_compilation_summary` / `wait_for_idle` / `get_tags` / `ensure_tag` / `add_tag` / `remove_tag` / `get_layers` / `ensure_layer` / `add_layer` / `remove_layer` / `get_windows` / `focus_window` / `get_selection` / `get_active_tool` / `set_active_tool` / `get_project_root` / `drain_agent_input`；另有 4 个已弃用动作保留兼容。

`refresh` / `request_compile` 的关键参数：`console_mode`（`none|errors|full`，默认 `errors`；`errors` 只回错误+异常、无栈、相同项合并计数、最多 50 组，完整日志落盘）、`include_console`（旧参数）、`timeoutSeconds`、`clearCache`。

**manage_gameobject**：`find`（target/searchTerm/searchMethod/searchInactive/findAll）、`create`（primitive、parent、TRS、tag、layer、componentsToAdd、saveAsPrefab）、`modify`、`delete`、`get_components`、`remove_component`、`set_component_property`、`list_children`、`ensure_component`、`ensure_renderer_material`、`ensure_mesh_collider_mesh`、`ensure_prefab_default_sprite`。查找支持名称、层级路径、instanceID，`findAll` 返回全部。

**manage_asset**：`create`（路径+类型+属性）、`modify`（材质支持 shader 属性名与 color/float/texture/metallic/smoothness 别名；零变更视为失败）、`search`（pattern/path/类型/日期/分页/预览）、`get_info`、`ensure_prefab_variant`。

**manage_shader**：`compile`（paths / shader_names / material_paths / keywords / include_passes / variant_mode）、`preview`（mesh、resolution 64–1024、`frames:[{t,props}]`）。

**read_console**：`get`（types/count/filterText/format/collapse）、`clear`（`errors_only` 受 Unity API 限制实际全清）、`set_collapse`。

**execute_menu_item**：`execute`、`get_available_menus`（反射 + `[MenuItem]` 扫描）。

**执行脚本三兄弟**：参数 `script` 或 `script_path`、`imports`、`summary`、`timeoutSeconds`（默认 300，上限 3600，0 = 无上限）、`capture_logs`、`record_game_view`。执行引擎是 Roslyn（`Plugins/Codely.Roslyn.dll`），支持返回 `Task`、`IEnumerator`、`Task<T>`；超时会返回 `timed_out_stopped` / `timed_out_still_running` / `timed_out_finished` 语义。

**manage_package**：`install_package`（id/name/git URL + version）、`remove_package`、`list_packages`。

**manage_bake**：`bake_lighting`、`bake_navmesh`（可选 async）、`clear_navmesh`、`clear_baked_data`、`wait_for_bake`（已弃用）。

**manage_window_bridge**：`list_windows`、`focus_window`、`resolve_native_window`、`input`、`start_stream_server`、`stop_stream_server`、`get_stream_server_status`、`start_offscreen_stream`、`stop_offscreen_stream`。

**manage_gameview**：`get_resolution`、`set_resolution`（新建或选择固定尺寸 1–8192）、`list_resolutions`。

**manage_screenshot**：`capture_scene_view`、`capture_scene_camera`、`capture_main_camera`、`capture_specific_camera`、`capture_asset`、`capture_ui_toolkit`、`start_game_view_recording`、`finish_game_view_recording`。
- `view`：`current|front|back|left|right|top|bottom|iso|cardinal|all`；`cardinal` 是 2×2 拼图，`all` 是 3×3 七视图拼图。
- `as_gif`：`{frameCount, fps, colorCount, startDelay}`。
- `over_time`：`"N"` 或 `"NxS"` 多帧拼图。
- Play Mode 静态截图必须显式 `allow_static_in_play_mode=true` + `static_reason`。
- MP4 录制需要 ffmpeg；`durationSeconds` 1–8、`fps` 5–30、`encodeFps` 1–30、`scale` 0.25–1.0。

**manage_input**：`create_virtual_devices` / `destroy_virtual_devices` / `mouse_click` / `mouse_move` / `mouse_drag` / `mouse_scroll` / `mouse_down` / `mouse_up` / `key_press` / `key_down` / `key_up` / `type_text` / `sequence`（`steps`、`start_delay`、`step_delay`、`stop_on_error`）。

**manage_dialog**：`click`（button 正则 + title 正则），在后台泵执行，因此主线程被模态框卡住时仍可用。

**manage_job**：`list`、`status`、`cancel`；线程安全。

---

## 5. 执行引擎内部机制（决定“功能等价”的难点）

1. **命令分类**：主线程 vs 后台（只有 job/dialog）。
2. **同步响应**：handler 返回对象 → `Response.Success/Error` 包装。
3. **异步作业**：
   - `JobContext` 表示“已启动作业，稍后回包”；
   - `AsyncTaskRunner` 推进 `Task`/`Task<T>`；
   - `CoroutineRunner` 推进 `IEnumerator`（支持 `WaitForSeconds`/`WaitForEndOfFrame`/`WaitForFixedUpdate` 等）；
   - `StepJobRunner` 每帧推进一步，且**跨域重载存活**（状态持久化到 SessionState/文件）；
   - `DetachedJobs` 支持“客户端断开后继续跑、稍后查询”；
   - 统一超时码：`timed_out_stopped` / `timed_out_still_running` / `timed_out_finished`。
4. **写保护**（`WriteGuard`）：Play/Paused 期间拒绝会改项目状态的命令；`exec_editor_script` 会先退出 Play Mode（丢弃运行状态），`exec_runtime_script` 会自动进入 Play Mode。
5. **响应体积**：`ResponseSizeCap` 对绝大多数命令限制响应大小并把完整内容写文件；少数固定体积命令豁免。
6. **控制台采集**：`ReadConsole` 挂 `Application.logMessageReceived`，维护环形缓冲，支持折叠计数与落盘。
7. **状态快照**：`StateComposer` 组合播放模式、编译中、活动场景、控制台错误、策略标志。
8. **原生对话框**：Win32 / macOS 两套扫描器，后台线程点击。

---

## 6. 与“重做一个 MCP”直接相关的取舍

| 能力 | 等价难度 | 说明 |
|---|---|---|
| 场景 / GameObject / Asset / Console / Menu / UPM / GameView / GUID | 低–中 | 纯 `UnityEditor` API，可独立重写 |
| 编辑器状态、播放模式、刷新/编译 | 中 | 难点是跨 domain reload 的“运行到完成”体验 |
| 截图（PNG） | 中 | 场景视图渲染到 RT + `EncodeToPNG` |
| GIF / MP4 | 高 | 需要自写 GIF 编码或依赖 ffmpeg |
| `exec_editor_script` / `exec_runtime_script` | 高 | 需要 C# 动态编译 + Task/IEnumerator 驱动 + 超时语义 |
| `manage_input` | 高 | 依赖 Input System 虚拟设备或 legacy 注入，且要与 uGUI/EventSystem 对齐 |
| `manage_window_bridge` / 画面流 | 极高 | Win32/Cocoa 原生窗口枚举、WebRTC 数据通道、原生 DLL；不建议自研 |
| `manage_dialog` | 高 | 需要 Win32/Cocoa 模态框枚举与跨线程点击 |
| `manage_bake` | 低 | 官方 API 直接可用 |

结论：**“21 个工具全量 1:1”不是一次交付能达成的**。可先做到“日常 90% 工作流等价”，把原生窗口流、模态框点击、MP4 录制放到后续阶段。

---

## 7. 独立 MCP 方案（拟）

```
AI 客户端（Claude Code / Cursor / Codex / 任意 MCP 客户端）
   │ stdio（MCP JSON-RPC 2.0）
   ▼
unity-mcp-server（Node/TypeScript，@modelcontextprotocol/sdk）
   │ TCP 127.0.0.1:<port>，4 字节小端长度 + UTF-8 JSON
   ▼
Unity Editor 包（纯 managed，无原生依赖，Editor-only asmdef）
   │ EditorApplication.update 主线程泵
   ▼
UnityEditor API
```

设计要点：

1. **零原生依赖**：`TcpListener` + 长度前缀帧，Windows/macOS/Linux 一致；不引入闭源 DLL。
2. **发现**：Unity 侧写 `<ProjectRoot>/Temp/.unity-mcp.json`（端口、pid、projectPath、state、version）；MCP 侧优先读它，支持 `UNITY_MCP_PORT` / `UNITY_MCP_PROJECT` 覆盖，多实例按 projectPath 选。
3. **抗域重载**：长操作在 Unity 侧拆成状态机（发起 → 每帧推进 → 完成回包）；MCP 侧断线后重读心跳、等待新端口并重新提交/查询。对 AI 客户端仍表现为一次阻塞调用。
4. **写保护**：保留“Play Mode 拒绝写”语义，但由 Unity 侧统一判定，MCP 侧只做前置提示。
5. **脚本执行**：首期用编辑器内置 `Microsoft.CSharp.CSharpCodeProvider` 编译到内存（零依赖），入口约定 `static object Run()` / `static Task<object> RunAsync()`；后续再评估 Roslyn。
6. **JSON**：Unity 侧自带轻量解析/序列化（避免 Newtonsoft / System.Text.Json 依赖）。
7. **MCP 工具形态**：与 Cowork 对齐为 `manage_scene`、`manage_editor`… 这类“一个工具 + action 参数”，而不是拆成 60 个小工具，便于复用同一套行为契约；同时给每个 action 生成 JSON Schema。

### 分期

- **P1 核心闭环**：传输/发现/心跳、`list_tools`、`manage_editor`（状态/播放/刷新/编译）、`manage_scene`、`manage_gameobject`、`manage_asset`、`read_console`、`execute_menu_item`、`manage_package`、`manage_gameview`、`manage_guid`、`manage_screenshot`（PNG）、`exec_editor_script`。
- **P2**：`exec_runtime_script`、`manage_bake`、GIF、`manage_job`、`manage_dialog`。
- **P3**：`manage_input`、MP4、`manage_window_bridge`（评估是否值得做）。

---

## 8. 安全与合规

- 原包 `package.json` 未声明 license（README 写 MIT，但元数据缺失）；本方案**不复制其 C# 实现**，只对齐外部行为契约（工具名、动作、参数语义），实现独立编写。
- 新通道默认只监听 `127.0.0.1`；不引入远程访问；不写任何密钥。
- 写保护、Play Mode 门槛、路径限制（`Assets/`、`Packages/`）在 Unity 侧强制执行，不依赖客户端自律。

---

## 9. 实际交付（2026-11-21）

已按第 7 节方案落地，并与 Cowork Bridge 并存运行（不同端口，互不干扰）。

### 9.1 交付物

| 位置 | 内容 |
|---|---|
| `Packages/com.codely.unity-mcp/Editor/` | Unity 侧（纯 managed，Editor-only asmdef）：`Core/`（MiniJson、帧协议、TCP 服务器、控制台缓冲、写保护、路径）、`Tools/`（每个工具一个文件）、`UI/`（Tools > Codely > Unity MCP 状态窗口） |
| `Packages/com.codely.unity-mcp/Server~/` | Node/TypeScript MCP stdio 服务器（`src/` + 已构建 `dist/`） |
| `docs/unity-mcp/bridge-analysis.md` | 本报告 |
| `Packages/com.codely.unity-mcp/README.md` | 安装、MCP 客户端配置、排查 |

### 9.2 实际工具（13 个）

`project_overview`、`list_tools`、`manage_editor`、`manage_scene`、`manage_gameobject`、`manage_asset`、`read_console`、`execute_menu_item`、`manage_package`、`manage_gameview`、`manage_guid`、`manage_screenshot`、`exec_editor_script`。

相比原 Bridge 的 21 个，未实现：`manage_bake`、`execute_custom_tool`/`get_custom_tools`、`manage_window_bridge`、`manage_input`、`manage_dialog`、`manage_job`，以及截图中的 GIF/MP4/asset/UI Toolkit 分支。

### 9.3 传输与发现

- TCP `127.0.0.1`，4 字节小端长度 + UTF-8 JSON；单帧上限 64 MiB。
- 发现文件：`Temp/.codely-unity-mcp.json`（`port`、`state`、`pid`、`unity_version`、`last_error`…）。
- `state` 取值：`ready` / `reloading` / `stopped` / `error`。
- 端口按项目持久化到 `EditorPrefs`，默认 27180；占用时顺序探测并回退到随机端口（实测域重载后从 27180 迁到 27181 并被客户端自动跟随）。

### 9.4 域重载可靠性

- 重载前写 `state=reloading` 并关闭监听；重载后 `EditorApplication.update` 持续重试启动，直到编辑器空闲并绑定成功（首次实现只尝试一次，实测会静默停摆，已修）。
- 长操作（play/stop/refresh/request_compile/package）采用「立即返回 started + `manage_editor.get_operation` 轮询」；MCP 侧封装成一次阻塞调用，断线自动重连并继续轮询。
- 实测：`manage_editor.refresh` 在 761 ms 内返回 done，并附带控制台摘要。

### 9.5 脚本执行

- `exec_editor_script` 使用编辑器内置 `CSharpCodeProvider`（Mono `mcs`），零外部依赖。
- 关键修复：必须引用 `Facades/netstandard.dll`，否则生成的代码连 `System.Object` 都无法解析（CS0012）；引用列表只保留核心程序集与 `Assembly-CSharp*`，避免 mono 命令行超长。
- 限制：同步执行，`Task`/`IEnumerator` 以对象形式返回，不做等待。

### 9.6 已验证项

通过真实 MCP stdio 客户端验证：`tools/list`（13）、`resources/list`（4）、`prompts/list`（2）、`project_overview`、`manage_editor.get_state`、`manage_scene.get_hierarchy`、`manage_gameobject.find/create/set_component_property/delete`、`manage_asset.search/get_info/create(材质 color/metallic)`、`read_console`、`manage_package.list_packages`、`execute_menu_item.get_available_menus`、`manage_gameview.list_resolutions`、`manage_guid`、`manage_screenshot`（返回内联 PNG）、`exec_editor_script`、`manage_editor.refresh`（跨域重载）。

### 9.7 已知限制

- `manage_gameview` 依赖反射；`selectedIndex` 在不同编辑器版本语义不同，因此输出只给 group 内索引，设置时按宽高匹配。
- `execute_menu_item.get_available_menus` 只能反射到 `[MenuItem]` 声明的菜单，Unity 内置菜单栏是动态构建的，因此额外内置了一份常用路径。
- 截图 `view` 仅支持 `current`，其余方向返回 `unsupported_view`。
- 无输入模拟、原生窗口流、模态框点击、烘焙、自定义工具注册。
