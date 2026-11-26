# AGENTS.md

本文件面向在本仓库中工作的 AI Agent（Claude Code / Cursor / Codex / Codely 等）。
目标：**不读遍源码**也能知道这是什么项目、代码在哪、怎么改、怎么验证、哪里会踩坑。

> 最后核对：2026-11-22（Unity 已重新打开，编译 0 error，仅 1 条 Account API 网络警告）。

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
| 0 | `Assets/Scenes/登录场景.unity` | 登录/标题动画（8.47s 标题卡 + 故障层） |
| 1 | `Assets/Scenes/游戏大厅.unity` | 主界面（当前默认打开） |
| 2 | `Assets/Scenes/设置_异步窗口.unity` | 设置（Additive 加载或 Single 跳转） |
| — | `Assets/Scenes/URP2DSceneTemplate.unity` | 模板，未入 Build |

规模（2026-11-22 实测）：**项目脚本 29**；`Assets` 下 MonoScript 总数 205（多为 TJGenerators）；PNG 140；着色器文件 21；动画剪辑 4；ScriptableObject 16；Prefab 0；材质 `t:Material` 17（`Assets` 下 `.mat` 4）。

---

## 2. 目录职责

| 路径 | 内容 |
|---|---|
| `Assets/Scenes/` | 三个正式场景 + 一个模板 |
| `Assets/动画/` | `.anim`、`.controller`；`Assets/动画/脚本/` 是**中文文件名、英文类名**的控制器脚本 |
| `Assets/分辨率控制器/` | `ResolutionAdapt`、`BackgroundFit`、`ResolutionAdaptToggle` |
| `Assets/StreamingAssets/` | 目前为空；`EncryptedAssets/` 已随防逆向模块于 2026-11-26 一并移除 |
| `Assets/美术素材/` | 圆角 SDF 精灵、AppIcon、背景等 |
| `Assets/文字/` | TMP 字体与文本资源 |
| `Assets/全局异步窗口ID.cs` | `WindowDatabase`（异步窗口 + 跳转窗口两份列表） |
| `Assets/cn.tuanjie.ai.generators/` | TJGenerators 第三方包（独立 asmdef，**不要改**） |
| `Assets/Welcome/` | 2D 模板欢迎页（独立 asmdef） |
| `Assets/Settings/` | URP / Input System 等项目设置资产 |
| `Packages/com.codely.unity-mcp/` | 自研 MCP 桥（Unity 侧 + `Server~/` Node MCP） |
| `docs/unity-mcp/` | Bridge 拆解与 MCP 实现说明 |
| `CODELY.md` | 项目记忆（场景结构、动效参数、已知缺陷） |

项目脚本没有自己的 asmdef，默认进 `Assembly-CSharp`；只有第三方/模板包有 asmdef。

---

## 3. 脚本映射（按类名找文件，别按英文文件名猜）

| 类 | 文件 |
|---|---|
| `WindowDatabase` | `Assets/全局异步窗口ID.cs` |
| `LobbyIntro` | `Assets/动画/脚本/切场过渡动画控制器.cs` |
| `AnimSceneTransition` | `Assets/动画/脚本/动画场景效果器.cs` |
| `TextReveal` | `Assets/动画/脚本/文字效果器.cs` |
| `UIMenuButton` | `Assets/动画/脚本/游戏主页_4按钮动画控制器.cs` |
| `UIMenuPopIn` | `Assets/动画/脚本/缩放适配控制器.cs` |
| `GlitchRamp` | `Assets/动画/脚本/雪花效果控制器.cs` |
| `SettingsWindowManager` | `Assets/动画/脚本/异步窗口管理.cs` |
| `SettingsWindow` | `Assets/动画/脚本/SettingsWindow.cs` |
| `SimpleToast` | `Assets/动画/脚本/SimpleToast.cs` |

完整 29 条映射见 `docs/unity-mcp/bridge-analysis.md` 第 10.3 节（防逆向与密钥相关类型已于 2026-11-26 删除，该文档对应条目为历史快照）。

---

## 4. 核心系统

- **登录标题卡**：Animator 驱动。`骸歌` 字形 + `白条` 擦除；`文字信息.anim` 控制副标题（RectMask2D 擦除）；`故障.anim` + `GlitchRamp` 驱动故障层。总时长 8.47s，末尾硬切为 0。
- **大厅菜单**：`UIMenuButton` 在 `Canvas/菜单/开始游戏|继续游戏|存档|设置` 四个实例；字段含 `打开窗口ID`、`窗口是否为跳转`、悬停/按下缩放、文字常态/悬停色。
- **设置窗口**：`WindowDatabase` 持 `configs`（异步）与 `jumpConfigs`（跳转）；`SettingsWindowManager.打开设置(id, 是否跳转)`：false = Additive + 开窗动画，true = `LoadSceneAsync` Single。`SettingsWindow` 管面板/遮罩/ESC；`SimpleToast` 管提示。
- **分辨率适配**：`ResolutionAdapt` 默认开（PlayerPrefs `resolution_adapt`）；`BackgroundFit` 以 2400×1080 做 cover，父节点必须有 `背景裁切` 的 `RectMask2D`。
- **防逆向与密钥**：已于 2026-11-26 整套删除（运行时校验、密钥仓库、LFLN 加密、编辑器加密工具、`StreamingAssets/EncryptedAssets`、`DeviceKey`/`DeviceKeyPanel`/`SpecialKeyInput` 及设置场景中的密钥 UI）。开发阶段稳定性未验证，先不引入；需要时从 git 历史取回。

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
3. **圆角精灵**（`圆角按钮/面板/卡片/卡片描边.png`）由 SDF 生成：形状必须向纹理内部留 4~6px inset，九宫格 border = inset + radius，否则边缘被裁掉一像素。
4. **Play Mode 写保护**：MCP/编辑器工具在播放中拒绝改项目状态，先 `stop`；运行时改动会被 Unity 丢弃。
5. **域重载**：`refresh` / `request_compile` / 装包会触发重载并断开 MCP 连接；客户端会自动重连，长操作轮询完成标记，不要重复发起。
6. **登录场景有一个空的 `TextReveal` 根物体**，每次播放报“未指定文本组件”警告，属已知噪音。
7. 设置场景既可 Additive 也可 Single，判断依据是 `打开设置(id, bool)` 的第二个参数，不要假设只有一种。
8. **`inspect_script` 按类名解析**：本项目中文文件名不能用文件名搜索，工具内部走 `MonoScript.GetClass()` 缓存；新增脚本后若类型找不到，先让编辑器编译再查。

---

## 8. 验证与构建

- **编译**：改 C# 后让编辑器导入（MCP `manage_editor.refresh` 或 `request_compile`），然后 `read_console` 必须 0 error。
- **测试**：仓库没有项目级测试套件；只有 `Assets/cn.tuanjie.ai.generators/Tests/Editor/` 自带测试，用 Unity Test Runner 运行。
- **构建**：`File > Build Settings`（无 CI/命令行构建脚本）。Android 构建依赖已安装的 Android Build Support。
- **不要**臆造 `npm test` / `dotnet test` 之类的命令。

---

## 9. 文档索引

- `docs/unity-mcp/bridge-analysis.md` — Cowork Bridge 拆解 + 自研 MCP 实现/验证/限制 + **项目现状快照与 29 条脚本映射**
- `Packages/com.codely.unity-mcp/README.md` — MCP 安装、客户端配置、工具清单、排错
- `CODELY.md` — 项目记忆：登录场景动效参数、设置架构、圆角精灵规格（防逆向章节已标注为历史记录）

---

## 10. Agent 工作流（照做即可）

1. 读本文件；需要细节再读 `CODELY.md` 对应段落。
2. 有 MCP：`project_overview` → `project_map` → `tool_help`（目标 action）。
3. 定位目标：`manage_scene.get_hierarchy`（看 `key_objects`）→ `manage_gameobject.find` / `manage_asset.search` / `inspect_script`。
4. 改动：优先专用工具；字段名只取 `field_schema` 或 `inspect_script.fields`，不要猜。
5. 验证：`read_console`（看 `diagnosis`）→ `get_state`；视觉问题才截图。
6. 汇报：给出改动的文件/对象路径、使用的工具、验证结果；未验证的部分明确说明。
