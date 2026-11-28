# 剧情脚本语法规范（PrologueScript DSL）

本文描述 `Assets/VisualNovel/Stories/PrologueScript.txt` 使用的脚本文本格式，由 `Assets/VisualNovel/Editor/NovelPrologueBuilder.cs` 解析生成 ScriptableObject 剧情（`Prologue.asset`）。

> 本文只讲剧本文本语法。Lua 自定义演出是独立能力，规范见 [visual-novel-lua.md](visual-novel-lua.md)；框架总览见 [visual-novel-framework.md](visual-novel-framework.md)。

> 这是**生成器专用**的中间格式，不是运行时格式。运行时只读 `.asset`。剧情编辑器窗口创建的剧情不使用本语法。
> 规范以当前代码为准；未列出的写法都不支持。

## 1. 工作流

1. 编辑 `PrologueScript.txt`（UTF-8）。
2. `工具 > 视觉小说 > 由剧本生成序章`：强制重新导入源文件 → 解析 → 重建节点。
3. `工具 > 视觉小说 > 检查序章结构`：节点数、校验错误、不可达、关键文本唯一性。
4. `工具 > 视觉小说 > 把序章接入运行场景`（改过 storyId 或场景引用时才需要）。

生成器会**删除并重建** `Prologue.asset` 里的所有节点，因此不要在编辑器里手动改该资产的节点（会被覆盖）。变量与角色表目前写在生成器代码里，不从脚本声明。

## 2. 文件格式

- 编码 UTF-8，扩展名任意（当前为 `.txt`），按行解析。
- 每行先做 `Trim()`；**空行被忽略**。
- 以 `//` 开头的行是注释。
- 行首字符决定类型：`@` 指令、`- ` 选项、其它为台词/旁白。

没有被支持的转义。台词里可以出现 `@`、`-`、`#`、`{}` 等字符，但**出现在行首**时会被当作指令/选项；如需以 `- ` 或 `@` 开头说话，目前只能改写或扩展生成器。

## 3. 流程模型

脚本按**书写顺序**自动串联：

- 每生成一个节点，就把上一个「可流动节点」（对话 / 输入 / 抽样 / 命令 / 条件）的 `next` 指向它；上一个若是条件节点且 `whenFalse` 未定，则填 `whenFalse`。
- `@choice`、`@goto` 会结束当前顺序连接（`@choice` 之后必须用 `@label` 重开链）。
- `@end` 生成结局节点，不参与后续串联。

`@label <名字>` 不生成节点，而是把标签绑定到**紧随其后的那个节点**。因此标签必须写在目标内容之前：

```text
@label after_help
@speaker 未知人物
随后…
```

标签解析在整份脚本解析完成后统一进行；指向不存在标签会抛异常并中止生成。

## 4. 指令一览

### 4.1 `@speaker`

```text
@speaker 勒特
@speaker -
```

设置后续台词行的说话人，直到下一个 `@speaker`。`-` 表示旁白（`characterId` 为空）。

- 名字必须是生成器中已注册的角色 ID，否则校验器报「未知角色」。
- 当前角色：`勒特`、`勒特？`、`末戮`、`未绥`、`墨离`、`月映知`、`狼少`、`未知人物`、`军官`、`喀兹士兵`、`游击队队员`。

### 4.2 台词行

```text
@speaker 勒特
他们的时间不多了。
```

任何非指令、非选项行都生成一个 `Dialogue` 节点：

- `text` 为整行内容（原样，允许 `{变量}` 占位，运行时替换）。
- `characterId` 为当前 `@speaker`。
- 文本不能为空（空行会被跳过，所以不会产生空台词）。

### 4.3 `@label`

```text
@label item1
```

标记跳转点，绑定到下一个生成的节点。同一脚本内标签不可重复（重复时后者覆盖前者，不会报错，请自行避免）。

### 4.4 `@goto`

```text
@goto after_item
```

把**上一个节点**的 `next` 指向标签；若上一个节点是条件节点，则改写其 `whenFalse`。`@goto` 之后顺序链断开，需要 `@label` 继续。

`@goto` 前必须已有节点，否则生成中止。

### 4.5 `@choice`

```text
@choice 【请选择你要带走的信物】
- 【布制护身符】 -> item1 [if s1] [set souvenir=布制护身符]
- 【032号金属手环】 -> item6 [set souvenir=032号金属手环]
```

`@choice` 后跟提示文本（必填），生成 `Choice` 节点；紧随其后的 `- ` 行依次成为选项。

选项语法：

```text
- <显示文本> -> <目标标签> [if <布尔变量>] [set <变量>=<值>] [set ...]
```

| 部分 | 说明 |
|---|---|
| `显示文本` | 必填，非空 |
| `-> 目标标签` | 必填，必须已定义 |
| `[if 变量]` | 可选；变量必须为已声明的 **布尔** 类型，为 true 时该选项才出现 |
| `[set 变量=值]` | 可选，可重复；`值` 按变量声明类型解析（字符串原样、布尔 `true/false`、整数/浮点用 InvariantCulture） |

约束：

- 选项数量不限，但运行时若条件过滤后为空会进入 Faulted，请保证至少一个无条件选项。
- 选项文本同样支持 `{变量}` 占位。
- `[if]` 只能判断布尔变量为 true；比较运算请用 `@if` 节点。

### 4.6 `@if`

```text
@if souvenir == 什么都不拿 -> leaving
勒特的手指刚触碰到那个东西——
@goto convulse

@label leaving
勒特转过身，准备离开的那一刻——
@label convulse
```

生成 `Conditional` 节点：

- 条件语法：`@if <变量> <运算> <值> -> <真分支标签>`，运算为 `== != > < >= <=`。
- `whenTrue` = 给定标签。
- `whenFalse` = **紧随其后生成的节点**（所以「假分支」内容要写在 `@if` 之后、真分支标签之前）。
- 比较两侧类型必须一致（值按变量声明类型解析）。

### 4.7 `@input`

```text
@input name 8
```

生成 `TextInput` 节点：

- 参数：`<字符串变量> <最大字数>`（1～64，按 Unicode 文本元素计数）。
- 提示文案由生成器固定为「（请输入你的名字）」，**脚本里不能自定义**。
- 运行时由玩家点「确认」提交；拒绝空白、`<>`、`{}`、换行；提交后写入变量并继续。

### 4.8 `@sample`

```text
@sample s1,s2,s3,s4,s5 2
```

生成 `SampleFlags` 节点（首次不放回随机抽样）：

- 参数：`<逗号分隔的布尔变量> <抽取数量>`。
- 首次进入时随机置 true 指定数量、其余 false，并记录该节点 ID；同局再次进入或读档不重抽，新游戏重置。
- 变量必须已声明为布尔且不重复。

### 4.9 `@black`

```text
@black
```

生成 `Background` 命令节点，资源 ID 固定为 `black`（`Assets/VisualNovel/Art/PureBlack.png`）。用于当前纯黑占位背景。

### 4.10 `@effect`

```text
@effect flash #FFFFFF 0.35
@effect shatter 1.4
@effect shake 26 30 0.9
@effect distortion #C8E2FF 2.4
@effect fade #000000 1
```

生成 `Command` 节点。参数：

| 名称 | 参数 | 生成字段 | 阻塞剧情 |
|---|---|---|---|
| `flash` | `#RRGGBB` 秒 | `Flash` + `color` + `seconds` | 否 |
| `shatter` | 秒 | `Shatter` + `seconds` | 否 |
| `shake` | 幅度 频率 秒 | `Shake` + `shakeStrength` + `shakeFrequency` + `seconds` | 否 |
| `distortion` | `#RRGGBB` 秒 | `Distortion` + `color` + `seconds`（幅度 10、频率 3） | 否 |
| `fade` | `#RRGGBB` 秒 | `Fade` + `color` + `seconds` | **是**（`waitForCompletion`） |

颜色必须能被 `ColorUtility.TryParseHtmlString` 解析（`#RRGGBB` 或 `#RRGGBBAA`）。

### 4.11 `@end`

```text
@end
```

生成 `End` 节点，`returnToLobby = true`（结局后回大厅）。一般放在文件末尾。

## 5. 变量表（序章）

变量**不在脚本里声明**，由生成器固定创建；脚本只能引用：

| 变量 | 类型 | 初值 | 用途 |
|---|---|---|---|
| `name` | string | `勒特` | `@input` 写入，用于 `{name}` 与说话人显示 |
| `memory` | int | 0 | 记忆选项 1 / 2 |
| `promise` | bool | true | 承诺「好 / 不好」 |
| `souvenir` | string | 空 | 信物选择结果 |
| `s1`～`s5` | bool | false | 前五具尸体抽样标记 |

新增变量必须在 `NovelPrologueBuilder.Build()` 的 `AddVariable` 处添加，然后重新生成。

## 6. 文本占位符

台词、角色显示名、选项文本支持 `{变量ID}`，运行时替换为当前值：

```text
@speaker 勒特
{souvenir}……我会记住的。
```

- 未声明的 `{xxx}` 原样保留。
- 替换值中的 `<`、`>` 会被替换为全角，避免注入富文本标签。
- 输入框本身拒绝 `{}`，因此变量值不会反过来制造占位符。

## 7. 完整示例

```text
// 最小可跑示例
@black

@speaker -
死寂。

@label ask_name
@speaker 未知人物
你…还记得自己叫什么吗？
@input name 8

@choice 能最后帮我们一个忙吗？
- 好 -> yes [set promise=true]
- 不好 -> no [set promise=false]

@label yes
@speaker 未知人物
我们的时间，不多了。
@goto converge

@label no
@speaker 未知人物
好吧，记住你的名字
@label converge

@if promise == true -> warm
@speaker 未知人物
（沉默）
@goto end

@label warm
@speaker 未知人物
谢谢你。
@label end
@speaker -
消逝吧。
@end
```

## 8. 生成期错误与校验

生成阶段直接抛异常（Console 可见）的情况：

- `@goto` 前没有节点。
- 选项行不在 `@choice` 之后。
- 选项行/`@if` 行语法不匹配正则。
- `@if`、`@set` 引用未声明变量。
- 标签未定义（统一解析时报「未解析的跳转标签：X」）。
- `@effect` 未知名称或颜色格式错误。

生成后由 `NovelStoryValidator` 检查（`检查序章结构` 菜单输出）：

- storyId 为空 / 版本非法；节点 ID 空或重复。
- 入口缺失；目标断链或不属于本剧情。
- 对话文本为空；未知角色。
- 条件变量不存在或类型不匹配；非布尔变量做加减。
- 选项为空、文本为空。
- 立绘槽位越界；立绘变换/震动参数非法。
- 命令缺资源（背景/立绘/CG 图片、音乐/环境音/音效音频）。
- 输入节点未绑定字符串变量、长度越界；抽样数量/变量非法；Lua 节点缺脚本或时限非法。

## 9. 当前不支持（需扩展生成器）

- 变量、角色、资源的声明（写死在 C#）。
- `CG`、`Ambient`、`AudioVolume`、`BackgroundTransform`、`Portrait*`、`Music/Sound`、`Wait`、`LuaEffect` 等节点指令：DSL 没有对应关键字，需要在剧情编辑器里手动加节点（每次重新生成会被清掉），或先扩展 `NovelPrologueBuilder`。其中 Lua 节点的脚本规范见 [visual-novel-lua.md](visual-novel-lua.md)。
- 自定义 `@input` 提示文案。
- 行首转义、多行台词、富文本标签校验、注释写在行尾。
- 说话人随剧情变化的名字（如谜之声）；需要新增角色或改用 `{变量}`。
