# AGENTS.md

本文件面向在本仓库中工作的 AI Agent（Claude Code / Cursor / Codex / Codely 等）。
目标：**不读遍源码**也能知道这是什么项目、代码在哪、怎么改、怎么验证、哪里会踩坑。

> 最后核对：2026-11-28（HEAD `9422485`，与 GitHub 远端一致；仓库启用 Git LFS）。

---

## 1. 项目概览

| 项 | 值 |
|---|---|
| 仓库 | `Lament-for-the-Nameless`（登录标题卡使用「骸歌」字形） |
| 引擎 | Tuanjie 6000.6.0f1（revision `f7f8ed4d1e24`），编辑器 `K:\Tuanjie\Hub\Editor\6000.6.0f1` |
| 渲染 | URP 17.6.0，Color Space = Linear |
| 脚本后端 | IL2CPP |
| 输入 / UI | Input System 1.20.0；uGUI 2.6.0 + TextMeshPro |
| 包源 | Tuanjie Packman `https://packages.tuanjie.cn` + Unity Registry |
| 产品 / 公司 | `Lament-the-Nameless` / `喵喵喵` |
| 形态 | 以 UGUI 界面/2D 素材为主，含登录 → 大厅 → 设置流程 |

场景（Build Settings）：

| index | 场景 | 用途 |
|---|---|---|
| 0 | `Assets/Scenes/Login.unity` | 登录/标题动画（8.47s 标题卡 + 故障层） |
| 1 | `Assets/Scenes/Lobby.unity` | 主界面（当前默认打开） |
| 2 | `Assets/Scenes/Settings.unity` | 设置（Additive 加载或 Single 跳转） |
| 3 | `Assets/VisualNovel/Scenes/NovelRuntime.unity` | ScriptableObject 剧情运行、分支、存读档与历史 |
| — | `Assets/Scenes/1.unity` | Lobby 遗留副本（含 WindowDatabase），未入 Build，可删 |
| — | `Assets/Scenes/URP2DSceneTemplate.unity` | 模板，未入 Build |

规模（2026-11-28 实测）：**项目脚本 58**（Animation 9 / ResolutionControl 4 / VisualNovel 44 / 根目录 WindowDatabase.cs；不含第三方与 Welcome 模板）；PNG 15；着色器 20；Prefab 1；材质 4；动画剪辑 4；Animator 控制器 4；TJGenerators 已移除。

---

## 2. 目录职责

| 路径 | 内容 |
|---|---|
| `Assets/Scenes/` | Login / Lobby / Settings；`1.unity` 为遗留副本可删；模板位于 `Assets/Settings/Scenes/` |
| `Assets/VisualNovel/` | 视觉小说框架：Runtime（Data/Playback/UI/Save/Settings）、Integration 旧页面适配、Editor 工具与验收、Tests；正式序章 `Stories/Prologue.asset`，源剧本 `Stories/PrologueScript.txt`，占位图 `Art/PureBlack.png` |
| `Assets/Animation/` | `.anim`、`.controller`、`Shader/`、`Materials/`；`Animation/Scripts/` 为控制器脚本（文件名与类名均为英文，类名见第 3 节） |
| `Assets/ResolutionControl/` | `ResolutionAdapt`、`BackgroundFit`、`ResolutionAdaptToggle`、`FrameRateSetting`（全局 60fps） |
| `Assets/Resources/` | 空目录（预留） |
| `Assets/_Recovery/` | Unity 崩溃恢复场景（`0.unity` 等，可删） |
| `Assets/场景连接图形编辑器/` | 场景连接图编辑器（**已实现**，入口 `工具/项目/场景连接图`，Ctrl+Shift+G）：扫描 Build 场景成卡片，行级出口 + 连线写回；设计文档 `场景连接图编辑器-设计.md` |
| `Assets/StreamingAssets/` | 空目录 |
| `Assets/Art/` | `Rounded*` 圆角 SDF 精灵、AppIcon、背景（`Background2400x1080` / `Rooftop`）、骸歌字形（`Haige.png`） |
| `Assets/Fonts/` | TMP 字体与文本资源 |
| `Assets/WindowDatabase.cs` | `WindowDatabase`（异步窗口 + 跳转窗口两份列表） |
| `Assets/Welcome/` | 2D 模板欢迎页（独立 asmdef） |
| `Assets/Settings/` | URP / Input System 等项目设置资产 |
| `Packages/com.codely.unity-mcp/` | 自研 MCP 桥（Unity 侧 + `Server~/` Node MCP） |
| `docs/unity-mcp/` | Bridge 拆解与 MCP 实现说明 |
| `CODELY.md` | 项目记忆（场景结构、动效参数、已知缺陷） |

项目脚本没有自己的 asmdef，默认进 `Assembly-CSharp`；只有第三方/模板包有 asmdef。

---

## 3. 脚本映射（文件名与类名均为英文）

| 类 | 文件 |
|---|---|
| `WindowDatabase` | `Assets/WindowDatabase.cs` |
| `LobbyIntro` | `Assets/Animation/Scripts/SceneIntroTransition.cs` |
| `AnimSceneTransition` | `Assets/Animation/Scripts/SceneTransitionEffect.cs`（过场播完发流程 ID） |
| `SceneFlowManager` | `Assets/Animation/Scripts/SceneFlowManager.cs`（流程 ID → 场景，查本场景 `WindowDatabase.jumpConfigs`） |
| `TextReveal` | `Assets/Animation/Scripts/TextRevealEffect.cs` |
| `UIMenuButton` | `Assets/Animation/Scripts/MenuButtonAnimator.cs` |
| `UIMenuPopIn` | `Assets/Animation/Scripts/MenuPopInAnimator.cs` |
| `GlitchRamp` | `Assets/Animation/Scripts/GlitchRampController.cs` |
| `SettingsWindowManager` | `Assets/Animation/Scripts/AsyncWindowManager.cs` |
| `SettingsWindow` | `Assets/Animation/Scripts/SettingsWindow.cs` |
| `SimpleToast` | `Assets/Animation/Scripts/SimpleToast.cs` |
| `ResolutionAdapt` | `Assets/ResolutionControl/ResolutionAdapt.cs` |
| `BackgroundFit` | `Assets/ResolutionControl/BackgroundFit.cs` |
| `ResolutionAdaptToggle` | `Assets/ResolutionControl/ResolutionAdaptToggle.cs` |
| `FrameRateSetting` | `Assets/ResolutionControl/FrameRateSetting.cs` |
| `NovelEntryPoint` | `Assets/VisualNovel/Integration/NovelEntryPoint.cs` |

---

## 4. 核心系统

- **登录标题卡**：Animator 驱动。`Haige.anim` 字形 + `WhiteBar.anim` 擦除；`TextInfo.anim` 控制副标题（RectMask2D 擦除）；`Glitch.anim` + `GlitchRamp` 驱动故障层。总时长 8.47s，末尾硬切为 0。
- **场景流程（登录出口）**：场景只播动画、不写死目标。`AnimSceneTransition` 播完发 `流程ID` → `SceneFlowManager.Play(id)` → 查本场景 `WindowDatabase.jumpConfigs` → `LoadSceneAsync(Single)`。登录链路为 `login_intro → Lobby`，这条边在场景连接图里可直接拖线改写。详见 `docs/login-intro-and-scene-flow.md`。判动用"动画时长为主 + Animator 播完为辅 + 最多等待兜底"，动画期间后台预加载目标场景；**不改任何 `.anim`/`.controller`/Animator 参数**。`直出直入` 是刻意设计：动画末帧自切黑，`LobbyIntro` 首帧黑场承接。
- **大厅菜单**：`UIMenuButton` 在 `Canvas/菜单/开始游戏|继续游戏|存档|设置` 四个实例；字段含 `打开窗口ID`、`窗口是否为跳转`、悬停/按下缩放、文字常态/悬停色。
- **设置窗口**：`WindowDatabase` 持 `configs`（异步）与 `jumpConfigs`（跳转）；`SettingsWindowManager.打开设置(id, 是否跳转)`：false = Additive + 开窗动画，true = `LoadSceneAsync` Single。`SettingsWindow` 管面板/遮罩/ESC；`SimpleToast` 管提示。
- **分辨率适配**：`ResolutionAdapt` 默认开（PlayerPrefs `resolution_adapt`）；`BackgroundFit` 以 2400×1080 做 cover（勿改成按 Sprite 比例，见第 7 节坑 8），父节点必须有 `背景裁切` 的 `RectMask2D`；`FrameRateSetting` 在 BeforeSceneLoad 设 vSync=0 + 60fps。
- **视觉小说**：`Assets/VisualNovel` 全量框架（运行 / 编辑器 / 存档 / MoonSharp Lua 演出）；正式序章 `Stories/Prologue.asset`。**改动剧情前必读第 9 节四份规范**；验收菜单在 `工具 > 视觉小说`。

---

## 5. 工具：优先用 MCP，不要盲改

仓库自带 `Packages/com.codely.unity-mcp`：Unity 侧监听 `127.0.0.1`，端口写入 `Temp/.codely-unity-mcp.json`；MCP 服务在 `Server~/dist/index.js`。共 **17 个工具**。

如果客户端已接入：

1. `project_overview` — 引擎/管线/活动场景/控制台健康/`project_scripts`
2. `project_map` — 文件夹职责、场景用途、16 个项目组件的字段与使用位置
3. `tool_help {tool, action}` — 参数、可复制的示例、返回结构、常见错误
4. `inspect_script {type|path}` — 类摘要、序列化字段、private 生命周期、挂载位置（**代替读 .cs**）
5. `find_usage` — 资源/组件的静态引用（动态加载看不到）
6. 操作后用 `read_console`（含 `diagnosis[]`）、`get_state`、必要时 `manage_screenshot` 验证

返回契约（用之前先看）：
- 每个响应带 `_meta{play_mode, can_write, blockers[]}`；`can_write=false` 时按 `blockers[].fix` 处理。
- 实体带 `ref`，可直接作为 `manage_gameobject` 的 `ref` 参数回传。
- `get_components` 的 `field_schema[name]` 给出 `type/settable/setter/enum_values`；字段名只从这里取。

任务配方（改 UI 文本、换材质、加按钮、调相机、查报错、找功能归属、改 Animator）见 MCP 资源 `unity://project/recipes` 或 `docs/unity-mcp/bridge-analysis.md`。

没有 MCP 时，按类名 `rg "class X"`，只读目标脚本；不要全库通读。

---

## 6. 代码约定

- **文件名可能是中文，类名是英文**：按类名搜索，不要按英文文件名猜路径。
- 新脚本放对应功能目录；不要为了“整齐”移动已有脚本或删除 `.meta`（GUID 引用会断）。
- UI 文本统一 TMP；中文缺字时先处理 TMP 中文字体回退（参考 `TMPChineseFont` 技能）。
- 场景/资源路径使用项目相对路径（`Assets/...`）。
- 修改场景对象用工具或 `Undo`，不要手改 `.unity` YAML。
- 不要改 `Library/`、`Temp/`、`Logs/`、`obj/`；不要提交 `*.apk`、`Library/`。
- 签名材料（keystore、若再出现 `Keys/`）**不得**写入日志、注释或提交。

---

## 7. 必须知道的坑

1. **Animator 不能驱动 TMP 的 `m_maxVisibleCharacters`**（int 字段，float 曲线被忽略，运行值恒 99999）。打字机效果用 `RectMask2D` 宽度动画，或拆分单字 + `CanvasGroup.alpha`。
2. **TMP `characterSpacing` 以 1/100 em 计**：55 ≈ 0.55em。
3. **圆角精灵**（`Assets/Art/RoundedButton / RoundedPanel / RoundedCard / RoundedCardOutline.png`）由 SDF 生成：形状必须向纹理内部留 4~6px inset，九宫格 border = inset + radius，否则边缘被裁掉一像素。
4. **Play Mode 写保护**：MCP/编辑器工具在播放中拒绝改项目状态，先 `stop`；运行时改动会被 Unity 丢弃。
5. **域重载**：`refresh` / `request_compile` / 装包会触发重载并断开 MCP 连接；客户端会自动重连，长操作轮询完成标记，不要重复发起。
6. 设置场景既可 Additive 也可 Single，判断依据是 `打开设置(id, bool)` 的第二个参数，不要假设只有一种。
7. **`inspect_script` 按类名解析**：脚本文件名已英文化（目录为 `Assets/Animation/Scripts` 等），类名保持不变；工具内部走 `MonoScript.GetClass()` 缓存，新增脚本后若类型找不到，先让编辑器编译再查。
8. **`BackgroundFit` 的 cover 以 2400×1080 参考分辨率为基准**，不要改成按 Sprite 原始比例：21:9 背景在 20:9 画面下会左右各溢出约 54.6px，Inspector 显示 Left/Right ≈ -54.58 属预期裁切而非拉伸。
9. **GraphView 的右键事件只发给 GraphView 自己**：`ContextualMenuPopulateEvent` 由 `ContextualMenuManipulator` 发给它挂载的元素，挂在 Node/Port/Edge 上的回调永远不会触发。要按对象出菜单，只能在 GraphView 的回调里用"最近一次指针位置 + 命中测试"分发（场景连接图已如此实现）。
10. **自研工具挂中文顶级菜单「工具」**：项目里存在 `工具/…`（中文字面量）与 Unity 本地化的 `Tools`→「工具」两个同名菜单，写 `Tools/场景连接图` 用户会找不到；统一用 `工具/项目/…`。
11. **过场跳转不要写死目标场景**：走 `AnimSceneTransition.流程ID` → `SceneFlowManager` → `WindowDatabase.jumpConfigs`，否则场景连接图看不到这条边，也无法拖线改。
12. **Git LFS 假"已修改"**：仓库对二进制（png/dll 等）启用了 LFS（`.gitattributes`）；reset / 切分支后若 `PureBlack.png`、`MoonSharp DLL` 等永远显示 modified，跑 `git lfs checkout` 或 `git reset --hard <commit>`，不要手动改这些文件。

---

## 8. 验证与构建

- **编译**：改 C# 后让编辑器导入（MCP `manage_editor.refresh` 或 `request_compile`），然后 `read_console` 必须 0 error。
- **测试**：仓库没有 Unity Test Runner 项目套件；视觉小说验收走编辑器菜单 `工具 > 视觉小说 > 运行…测试`（详见 `docs/visual-novel-framework.md`）。
- **备份 / 同步**：根目录 `备份到GitHub.bat`（add+commit+push）与 `从GitHub更新.bat`（fetch + fast-forward，脏工作区先 stash 后恢复）。远端 `https://github.com/Song-of-Bones/Lament-for-the-Nameless`。
- **构建**：`File > Build Settings`（无 CI/命令行构建脚本）。Android 构建依赖已安装的 Android Build Support。
- **不要**臆造 `npm test` / `dotnet test` 之类的命令。

---

## 9. 文档索引

- `docs/unity-mcp/bridge-analysis.md` — Cowork Bridge 拆解 + 自研 MCP 实现/验证/限制 + **项目现状快照与 29 条脚本映射**（2026-11-22 快照，规模数字以第 1 节为准）
- `docs/login-intro-and-scene-flow.md` — **登录开场动画与场景流程（SceneFlow）**：动画时间轴、`流程ID → SceneFlowManager → WindowDatabase` 链路、判动/预加载/跳过参数、场景连接图里怎么改
- `docs/build-and-release.md` — 构建与发布（含目录英文化后的路径修复记录）
- `docs/prologue-implementation.md` / `prologue-presentation-audit.md` — 序章实现与演出审计
- `Packages/com.codely.unity-mcp/README.md` — MCP 安装、客户端配置、工具清单、排错
- `CODELY.md` — 项目记忆：登录场景动效参数、设置架构、圆角精灵规格（防逆向章节已标注为历史记录）

**视觉小说规范（改动剧情内容前必须先读对应规范）：**

- `docs/visual-novel-framework.md` — 框架总览（运行时 / 编辑器 / 存档）
- `docs/node-reference.md` — 节点与演出命令参考（立绘 / 背景 / 镜头 / 音频 / 变量 / LuaEffect，含并行通道与存档规则）
- `docs/story-script-syntax.md` — PrologueScript DSL 语法（生成器专用中间格式；`.asset` 节点会被生成器重建，勿手改）
- `docs/visual-novel-lua.md` — MoonSharp Lua 演出规范（fx API / 沙箱限制 / 生命周期 / 读档重播）

---

## 10. Agent 工作流（照做即可）

1. 读本文件；需要细节再读 `CODELY.md` 对应段落。
2. 有 MCP：`project_overview` → `project_map` → `tool_help`（目标 action）。
3. 定位目标：`manage_scene.get_hierarchy`（看 `key_objects`）→ `manage_gameobject.find` / `manage_asset.search` / `inspect_script`。
4. 改动：优先专用工具；字段名只取 `field_schema` 或 `inspect_script.fields`，不要猜。
5. 验证：`read_console`（看 `diagnosis`）→ `get_state`；视觉问题才截图。
6. 汇报：给出改动的文件/对象路径、使用的工具、验证结果；未验证的部分明确说明。
