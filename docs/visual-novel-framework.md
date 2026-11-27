# 视觉小说框架（首阶段）

## 使用入口

- 正常流程：播放 `Assets/Scenes/Lobby.unity`，按现有双击确认交互选择“开始游戏”或“继续游戏”。
- 独立调试：打开 `Assets/VisualNovel/Scenes/NovelRuntime.unity` 后播放；该场景当前运行正式序章。
- 正式序章：`Assets/VisualNovel/Stories/Prologue.asset`（对照根目录《序章最终稿.docx》生成，背景与立绘为纯黑占位）。制作方式、抽样规则与缺口见 [prologue-implementation.md](prologue-implementation.md)。
- 示例内容：`Assets/VisualNovel/Stories/FrameworkDemo.asset` 与 `序章抽样组件示例.asset` 是工程功能演示，不属于正式剧情。
- 编辑器：`工具 > 视觉小说 > 剧情编辑器`。选择剧情资产后，可编辑剧情属性、新增子资产节点、编辑条件/选项/操作并校验。

## 目录结构（2026-11-27 英文化）

| 路径 | 内容 |
|---|---|
| `Assets/Scenes/` | `Login.unity`、`Lobby.unity`、`Settings.unity` |
| `Assets/Animation/` | 标题/菜单/故障动画；`Scripts/` 控制器，`Shader/`、`Materials/` |
| `Assets/Art/` | 圆角 UI 精灵、背景、AppIcon（当前仓库中多为 LFS 指针） |
| `Assets/Fonts/` | SourceHanSans 字体与 TMP SDF |
| `Assets/ResolutionControl/` | 分辨率适配组件 |
| `Assets/VisualNovel/` | Data / Playback / UI / Save / Settings / Stories / Scenes / Art / Lua / ThirdParty / Editor / Tests |
| `Assets/WindowDatabase.cs` | 窗口数据库 |

场景对象名与脚本内序列化字段名仍为中文（改动会断开场景绑定与动画路径），本次只英文化了文件与目录。

## 常用设置

`Assets/Scenes/Settings.unity` 的设置页包含：文字速度、自动播放间隔、音乐/环境音/语音/音效音量、全屏、分辨率自适应、恢复默认。

- 模型：`Assets/VisualNovel/Runtime/Settings/GameSettings.cs`，PlayerPrefs 键 `novel_text_speed`、`novel_auto_delay`、`novel_volume_music/ambient/voice/sound`、`novel_fullscreen`。
- 绑定：`Assets/VisualNovel/Runtime/UI/NovelSettingsPanel.cs`，挂在设置场景 `Canvas/窗口控制器`。
- 重建控件：`工具 > 项目 > 生成设置页控件`；检查：`工具 > 项目 > 运行设置页检查`（截图 `.codely-cli/test-results/settings-page.png`）。
- 剧情音量命令是相对值，最终音量 = 剧情音量 × 玩家设置音量；改设置会立即作用于正在播放的音轨。

## 操作

- 点击正文或按空格：先补全当前文本，再次操作推进。
- 自动：文本显示完成且语音结束后，等待 1.5 秒推进；计时器不会把文字未完成或语音播放中的时间累计进去。
- 已读快进：只推进进入当前节点之前已读过的对话；不会代选选项。
- 历史：滚动回看当前存档中的最近 500 条文本；不是回滚剧情状态。
- 存读档：3 个手动槽位。
- 快速存读：独立槽位 0；F5 快速存档、F9 快速读档。
- 设置：复用原有设置场景，打开时暂停剧情推进、打字显示、遮罩渐变和当前语音（BGM 保持播放）。
- 返回大厅：离开当前会话，不自动保存。请先保存所需进度。
- 继续游戏：选择最近一次有效存档；没有存档时开始新游戏。

## 模块边界

`Assets/VisualNovel/Runtime` 使用独立 `Lament.VisualNovel` 程序集，命名空间为 `Lament.VisualNovel`。

- 数据：`NovelStoryAsset` 与节点子资产 `NovelNodeAsset`，角色/资源目录、受限条件和变量操作。
- `NovelSession`：不依赖 MonoBehaviour 生命周期的节点状态机。对话、选择和等待为可存档检查点；每次同步最多执行 1024 个节点，防止错误剧情锁死主线程。
- `NovelVariableStore`：Boolean/Integer/Number/String 类型变量；只允许声明过的变量和类型匹配操作，不执行任意 C#。
- `NovelPlayer`：Unity 生命周期、自动/快进、存档服务和场景退出协调。
- `INovelPresentation` / `NovelPresentationController`：UGUI/TMP 表现适配，代码控制 TMP 可见字符，不通过 Animator 的 float 曲线驱动 int 属性。
- `Integration`：留在默认程序集，连接既有 `UIMenuButton`、`SettingsWindowManager`；核心程序集不依赖旧页面脚本。
- `Editor`：列表式剧情编辑器、校验、一次性工程装配及测试菜单。工程装配拒绝覆盖已存在的示例和场景。

## 编写剧情

1. 创建 `视觉小说 > 剧情` 资产，指定唯一 `storyId` 和 `schemaVersion`。
2. 在剧情编辑器中新增节点。节点 ID 初始使用 GUID；发布后不要随意变更已有节点 ID。
3. 指定入口和 `next` / `target` / `whenTrue` / `whenFalse`。所有目标必须属于同一剧情的节点列表。
4. 在剧情中声明变量，条件与操作使用同名变量和一致的值类型。
5. 对话的 `characterId` 可为空（旁白）；非空时必须能在角色目录找到。
6. 背景/立绘/音乐/音效命令通过资源目录 ID 引用素材；立绘槽位为 0～5，旧三槽布局保留，新增槽位可通过 pose 调整。角色定义与资源目录分离，立绘显示通过 Portrait 命令明确设置。
7. 演出命令统一使用 `seconds`、`easing` 和 `waitForCompletion`。时长 0 为立即执行；勾选等待后在命令节点暂停指定时间，不勾选则与后续剧情并行。等待期间可存档，读档后重建最终画面并等待剩余时间，不重播瞬态演出。
8. 校验并保存资产，点击“从入口预览”或选中节点后点击“从选中节点预览”。预览使用独立运行场景和默认变量，不执行目标节点之前的路径，不改写资源的入口节点，不加载或覆盖正式存档；停止播放后恢复原先的 `playModeStartScene` 设置。预览前若有未保存场景，Unity 会询问如何处理。播放中不提供热跳转，尚无可视化连线图。

## 立绘与页面演出

在剧情编辑器中新增 Command 节点，通过 Inspector 配置：

- `Portrait`：指定 `resourceId` 和 `portraitSlot`（0～5），`seconds > 0` 交叉淡化切换；0 立即切换。
- `PortraitExpression`：在剧情的 `characters[].expressions` 定义差分（例如 `id=smile` → `resourceId=hero_smile`），命令填写 `characterId`、`expressionId`、`portraitSlot`；支持立即/交叉淡化，保留槽位位置、缩放、旋转、透明度。差分图片仍统一登记在资源目录，校验器检查缺失图片及重复差分 ID；存档保存解析后的最终资源 ID。
- `HidePortrait`：指定槽位，按时长淡出后隐藏。
- `PortraitTransform`：`pose.position` 为相对槽位原始位置的 Canvas 参考像素偏移；`pose.scale` 为倍率（负 X 可镜像）；`pose.rotation` 为 Z 轴角度；`pose.alpha` 为 0～1。四种缓动：Linear/Smooth/EaseIn/EaseOut。
- `Background`：背景图像立即切换或按时长交叉淡化。
- `Fade`：遮罩渐变至 `color`，透明为淡入画面，不透明黑色为淡出。
- `Flash`：独立闪光层，从 `color` 衰减至透明；不覆盖长期 Fade 遮罩。
- `Shake`：背景和立绘产生衰减震动，不震动对话框/菜单；可配置幅度 `shakeStrength` 和频率 `shakeFrequency`。
- 不同槽位和不同演出通道可并行。同通道新命令替换旧命令；位置变换与震动为避免偏移冲突会互相取消；切换与变换同槽位同时使用有透明度竞争，应按节点先后等待编排。
- 暂停冻结所有演出计时；读档/新剧情取消瞬态通道。存档保存立绘最终位置/缩放/旋转/透明度和资源 ID，不保存切换残影、闪光或震动。旧存档没有 pose 数据时恢复默认变换。
- `工具 > 视觉小说 > 运行立绘特效测试` 使用临时内存夹具验证演出中间帧和恢复，报告 `.codely-cli/test-results/visual-novel-stage.txt`。当前还未制作带正式立绘资源的演出示范剧情。

## 序章专用能力补充

剧本逐段映射和仍缺的内容见 [prologue-presentation-audit.md](prologue-presentation-audit.md)。本次未修改根目录 DOCX，也未将全文导入剧情。

- `TextInput` 节点：绑定 `inputVariable` 字符串，确认后继续；正文、角色显示名、选项支持 `{变量ID}`。输入中禁止自动/快进和空格推进；显式确认避免 IME 回车选字误提交。
- `SampleFlags`：首次不放回抽取指定数量的布尔标记，结果和已抽样节点ID写入存档，同局/读档不重抽。`序章抽样组件示例.asset` 展示前五抽二、第六必经及四项信物筛选。菜单“运行序章抽样逻辑测试”执行50次完整逻辑验证，报告 `visual-novel-sampling.txt`。
- `ShowCG/HideCG`：独立保持比例的 CG/道具层，可淡入淡出；不替换背景、不挡操作控件。
- `BackgroundTransform`：背景镜头偏移/缩放/旋转，可配置时长/缓动。
- `Ambient/StopAmbient`：独立循环环境音；`AudioVolume` 指定 Music/Ambient/Voice/Sound 轨，按时长渐变到目标音量。
- `Distortion`：背景偏色错位重影；`Shatter`：背景4×4矩形碎片转场。两者不是真正不规则镜面后处理，读档取消瞬态。
- 存档兼容原三槽数组，补足六槽和新音量/镜头默认字段。环境音、BGM 读档从头播放；不保存音频采样游标。

## Lua 自定义效果

新增 `LuaEffect` 节点，可拖入 `.lua.txt` TextAsset，通过数学运算、循环和逐帧等待编排临时图像，不限于内置预设。API、执行预算、暂停/取消/读档重播规则与示例见 [visual-novel-lua.md](visual-novel-lua.md)。第一版仅临时视觉对象，不开放文件、网络、CLR 或剧情变量写入；不把进程内限制当成恶意脚本的强隔离。

## 存档与兼容

正式存档目录：`Application.persistentDataPath/VisualNovel/Saves`。

- JSON 只保存稳定剧情/节点/资源 ID、变量、已读记录、历史和背景/立绘/音乐/遮罩状态，不保存 Unity 实例 ID 或绝对资源路径。
- 写入先经过 `.tmp`，已有槽位用文件替换并保留 `.bak`。恢复只加载主文件，不会静默使用备份覆盖用户选择。
- 存档格式版本为 1。剧情版本必须与 `schemaVersion` 一致；不支持的版本明确拒绝，不自动迁移。
- 读档恢复在检查点，不重复执行已执行的变量操作、命令或写入历史。
- 不保存音频采样位置、正在显示的精确字符数或淡入淡出的中间帧。恢复时重建最终演出状态；未显示完的对话从本句重新显示。
- 已读集合属于当前会话/存档；尚未实现独立于存档的全局已读数据库。

## 验证

- `工具 > 视觉小说 > 运行核心测试`：51 项 Unity Test Runner EditMode 测试（含序章全流程、抽样与结构检查）；报告 `.codely-cli/test-results/visual-novel.xml`。
- 序章生成与验收：`工具 > 视觉小说 > 由剧本生成序章` → `把序章接入运行场景` → `运行序章界面检查` / `运行大厅到序章集成测试`。说明见 [prologue-implementation.md](prologue-implementation.md)。
- `工具 > 视觉小说 > 运行节点预览测试`：检查直接进入选中节点、默认变量、资源入口不变，以及停止后编辑场景/播放起始场景设置的恢复；报告 `.codely-cli/test-results/visual-novel-preview.txt`。
- `工具 > 视觉小说 > 运行演出恢复测试`：通过内存图像/静音音频夹具验证背景、立绘、音乐、语音、一次性音效、暂停/恢复和遮罩读档；不新增正式素材，不消耗生成积分；报告 `.codely-cli/test-results/visual-novel-presentation.txt`。
- `工具 > 视觉小说 > 运行序章组件测试`：实际验证输入UI射线点击、CG、六角色槽位、背景镜头、环境音渐变/暂停/读档、错位与碎片清理。报告 `.codely-cli/test-results/visual-novel-prologue.txt`。
- **当前不要运行构建验证菜单或打包。** 用户要求先完成功能；此前 Linux 构建期间编辑器退出，原因未确定。仅执行编译和编辑器内测试。
- `工具 > 视觉小说 > 运行大厅到剧情集成测试`：从大厅开始，经 UI 事件进入剧情，检查自动推进、已读快进边界、两条分支、变量、隔离 JSON 存读、历史/存档窗口暂停、设置打开/关闭、跨场景继续以及结束返回大厅；结束后退出 Play Mode。继续入口通过注入隔离存档服务测试，不读取或覆盖正式槽位。
- 新增控制/预览测试代码后，只有重新执行产生的新报告才代表当前代码通过；旧报告不代表新增检查已运行。编辑器无响应时可做编译检查，但不能替代 Unity Test Runner 和 Play Mode 验收。
- 集成测试报告：`.codely-cli/test-results/visual-novel-playtest.txt`。
- 截图：`.codely-cli/test-results/visual-novel-playtest.png`。
- 集成测试的隔离存档位于 `.codely-cli/test-results/playtest-saves`，不覆盖正式槽位。
- 编译/测试后继续检查 Unity Console，不能把菜单调用成功等同于测试通过。

## 当前边界

这是可继续扩展的首阶段框架，而不是 Ren'Py 的完整替代品。尚不包含：文本 DSL、跨故事调用栈、真正状态回滚、全局已读持久化、存档版本迁移/云同步、图形节点编辑器、Timeline 演出轨道、正式剧本导入、本地化文本表、配音口型，以及正式立绘/音乐等内容生产。编辑器可校验孤立节点和 ID，但不证明任意条件组合都可到达结局；运行时会对无可用选项或无等待循环报错并停止。
