# 节点参考（Command / 演出能力）

本文列出剧情编辑器里可直接配置的节点能力，重点是 `Command` 节点的演出参数。它不涉及脚本文本语法（见 [story-script-syntax.md](story-script-syntax.md)），也不涉及 Lua（见 [visual-novel-lua.md](visual-novel-lua.md)）。

## 通用规则

- 演出命令统一使用 `seconds`、`easing`、`waitForCompletion`：时长 0 为立即执行；勾选等待后在命令节点暂停指定秒数，不勾选则与后续剧情并行。
- 不同槽位、不同演出通道可以并行；同通道的新命令会替换旧命令。
- 位置变换与震动共用位置通道，会互相取消，避免偏移叠加。
- 切换与变换同时作用于同一槽位时存在透明度竞争，应按节点先后用等待编排。
- 暂停（历史、设置、存读档窗口）冻结所有演出计时；读档/新剧情取消瞬态通道。
- 存档保存立绘/背景的最终位置、缩放、旋转、透明度与资源 ID，不保存切换残影、闪光、震动等瞬态效果；旧存档缺少 pose 数据时恢复单位变换。

## 立绘

| 命令 | 关键字段 | 行为 |
|---|---|---|
| `Portrait` | `resourceId`、`portraitSlot`(0～5)、`seconds` | 立即或交叉淡化切换立绘 |
| `PortraitExpression` | `characterId`、`expressionId`、`portraitSlot` | 按角色差分表解析出资源 ID，保留槽位的位移/缩放/旋转/透明度 |
| `HidePortrait` | `portraitSlot`、`seconds` | 按时间淡出后隐藏 |
| `PortraitTransform` | `pose.position`、`pose.scale`、`pose.rotation`、`pose.alpha` | 相对槽位原始位置做变换；`scale` 为倍率（负 X 可镜像），`position` 单位为 Canvas 参考像素，`alpha` 0～1 |

- 差分在剧情的 `characters[].expressions` 配置（`id` → `resourceId`），图片仍统一登记在资源目录；校验器会检查缺失图片与重复差分 ID。
- 舞台共 6 个立绘槽：0～2 为原有布局，3～5 为扩展槽，可用 `PortraitTransform` 调整站位。
- 若角色定义了 `portrait` 且槽位 3 为空，表现层会在该角色说话时自动把它的占位立绘放进中间槽；脚本里的立绘命令优先。
- 四种缓动：`Linear`、`Smooth`、`EaseIn`、`EaseOut`。
- `工具 > 视觉小说 > 运行立绘特效测试` 用临时内存夹具验证中间帧、暂停、读档恢复与取消，报告 `.codely-cli/test-results/visual-novel-stage.txt`。

## 页面与镜头

| 命令 | 关键字段 | 行为 |
|---|---|---|
| `Background` | `resourceId`、`seconds` | 背景立即切换或交叉淡化（过渡期间保留旧图残影） |
| `BackgroundTransform` | `pose.*` | 背景镜头平移/缩放/旋转，用于推近、移开视线 |
| `Fade` | `color`、`seconds` | 遮罩渐变至指定颜色：透明=淡入画面，不透明黑=淡出 |
| `Flash` | `color`、`seconds` | 独立闪光层，按 `color` 衰减到透明，不覆盖长期 Fade 遮罩 |
| `Shake` | `shakeStrength`、`shakeFrequency`、`seconds` | 背景与立绘的衰减震动，不震动对话框/菜单 |
| `Distortion` | `color`、`seconds`、`shakeStrength`、`shakeFrequency` | 当前背景的偏色错位重影，结束或读档清理 |
| `Shatter` | `seconds` | 背景快照切成 4×4 矩形碎片，位移/旋转/淡出；非阻塞，可在后面接即时 `Background` 露出新场景 |

`Distortion` 与 `Shatter` 只是轻量近似：不是真正的不规则玻璃裂纹或粒子崩解，也没有后处理；需要更强效果时应扩展表现层或改用 Lua/专用美术。

## 图层与音频

| 命令 | 关键字段 | 行为 |
|---|---|---|
| `ShowCG` / `HideCG` | `resourceId`、`seconds` | 独立保持比例的 CG/道具层，可淡入淡出；夹在立绘之上、遮罩与对话之下，不挡点击 |
| `Music` / `StopMusic` | `resourceId` | BGM 循环播放/停止 |
| `Ambient` / `StopAmbient` | `resourceId` | 独立循环环境音，与 BGM/语音/音效分离 |
| `AudioVolume` | `audioBus`(Music/Ambient/Voice/Sound)、`volume`(0～1)、`seconds` | 音量渐变；最终音量 = 剧情音量 × 玩家设置音量 |
| `Sound` | `resourceId` | 一次性音效 |

- 环境音与 BGM 读档从头播放，不保存采样游标。
- 暂停时语音会暂停，BGM/环境音保持播放。

## 文本与输入

| 节点 | 关键字段 | 行为 |
|---|---|---|
| `Dialogue` | `characterId`、`text`、`voice` | 正文与说话人；`{变量ID}` 会按当前变量替换 |
| `TextInput` | `inputVariable`（字符串）、`inputMaxLength`(1～64) | 玩家显式确认后写入变量并继续；输入期间禁用自动/快进/空格推进 |
| `Choice` | `text`、选项列表 | 选项支持条件显示与选择后变量赋值 |
| `Wait` | `seconds`、`waitForInput` | 定时等待或等待玩家输入 |
| `Conditional` | `condition`、`whenTrue`、`whenFalse` | 按变量比较分支 |
| `SetVariable` | `operations` | 赋值 / 加减（仅数值） |
| `SampleFlags` | `sampleVariables`、`sampleCount` | 首次不放回随机抽样并记录节点 ID；同局/读档不重抽 |
| `LuaEffect` | `luaScript`、`luaTimeout` | 执行受控 Lua 临时演出，详见 [visual-novel-lua.md](visual-novel-lua.md) |
| `End` | `returnToLobby` | 结束剧情，可返回大厅 |

- `TextInput` 拒绝空白、`<>`、`{}`、换行；按 Unicode 文本元素计数。
- `SampleFlags` 在 `序章抽样组件示例.asset` 里有完整演示（前五抽二 + 第六必经 + 四项信物筛选），菜单「运行序章抽样逻辑测试」跑 50 次完整流程。
- 输入节点与抽样的 DSL 写法见 [story-script-syntax.md](story-script-syntax.md)。

## 存档与节点状态

- 对话、选择、输入、等待、阻塞型命令都是可存档检查点；读档恢复节点、变量、已读、历史与演出最终状态。
- 不保存：音频采样位置、精确可见字符数、切换残影、闪光、震动、重影等瞬态。
- 已读快进只跳已读文本，不代选选项；自动播放等待「文本显示完成且语音结束」后的设定间隔。
