# Lua 自定义演出

## 用途与入口

使用 MoonSharp 2.0 纯托管解释器，运行受控 Lua 5.2 风格脚本。它不是只接受固定命令的伪 Lua：可以使用局部变量、函数、循环、条件、table、math 等自行计算动画轨迹，创建临时图片进行自定义演出。

1. 创建 `.lua.txt` 文件，使 Unity 将其导入为 TextAsset。
2. 在剧情编辑器新增节点，type 选择 `LuaEffect`。
3. 将脚本拖入 `luaScript`，设置 `luaTimeout`（0.1～120 秒）。节点的 `next` 指向演出完成后的剧情。
4. 所需图片先加入本故事 `resources` 目录。
5. 保存并预览。LuaEffect 始终阻塞剧情，脚本返回后自动清理并进入 next；无预设时间的业务自动推进不会跳过脚本。

工程示例：`Assets/VisualNovel/Stories/LuaEffectDemo.asset`。
脚本：`Assets/VisualNovel/Lua/spiral_echo.lua.txt`。
若不存在示例资产，可运行 `工具 > 视觉小说 > 创建Lua演出示例`。示例是内嵌小色块的工程演出，不下载/生成收费资产，不改正式剧情。

## 演出 API

使用点调用，不使用冒号调用。所有图像都是本次脚本创建的临时对象，不是原始场景对象。

| API | 含义 |
|---|---|
| `fx.image(resourceId)` | 从故事资源目录创建图片，返回整数句柄 |
| `fx.portrait(slot)` | 克隆当前立绘槽图像到临时层，不修改/隐藏原立绘 |
| `fx.background()` | 克隆当前背景图像到临时层，不修改原背景 |
| `fx.set(handle, property, value)` | 设置临时图片的单个属性 |
| `fx.remove(handle)` | 删除图片，已删除句柄不再可修改 |
| `fx.wait(seconds)` | 暂停脚本指定秒数；0 表示至少让出一帧 |

可设置的属性：

- `x` / `y`：相对 Canvas 中心的参考像素位置。
- `width` / `height`：参考像素尺寸，0～8192，图片保持比例。
- `scale_x` / `scale_y`：-20～20；负值用于翻转。
- `rotation`：Z 轴角度。
- `alpha` / `r` / `g` / `b`：0～1。

临时层在演出遮罩下方、常规背景和立绘上方，不挡住 UI 点击。`fx.portrait/background` 克隆当前图像与几何参数；后续属性修改仍然只作用于克隆。想隐藏原立绘或切背景应在 Lua 节点前后使用既有剧情命令，避免脚本永久改坏舞台状态。

```lua
local h = fx.image("crystal")
fx.set(h, "width", 180)
fx.set(h, "height", 180)
for frame = 0, 90 do
    local t = frame / 90
    fx.set(h, "x", math.sin(t * math.pi * 4) * 200)
    fx.set(h, "y", t * 200)
    fx.set(h, "rotation", t * 720)
    fx.set(h, "alpha", 1 - t)
    fx.wait(1 / 30)
end
```

逐帧等待基于实际 `Time.unscaledDeltaTime`，不保证与离线固定帧率完全一致；如需严格时间同步应另行扩展只读时间 API，不把 frame/30 当精确音频时间码。

## 生命周期、读档与错误

- 每个 Lua 节点使用新的 VM，无跨节点 Lua 全局变量。
- 暂停（历史、设置、存读档窗口）冻结 Lua 等待和执行时间。
- 脚本正常返回、取消、新故事或退出场景会清理临时对象。
- Lua 期间允许存档：保存剧情节点与原舞台状态，不保存 VM/协程/临时图片；读档清理旧临时层，然后从该 Lua 节点脚本开头重播。
- 不提供修改剧情变量、存档、音频、SceneManager 或任意 Unity 对象的 Lua API，避免重播导致业务副作用。需要持久业务变化应使用节点/变量操作。
- 语法、运行、资源 ID 和预算错误包含故事/节点/脚本名，剧情进入 Faulted，不静默跳过；修复后重启预览或加载有效存档。

## 执行限制

- 不开放 `io`、`os`、`debug`、`require`、`load`、`dofile`、CLR、反射、进程、网络或第三方模块。
- 不注册任何 CLR userdata，只有明确的数字/字符串回调。
- 关闭用户可用的嵌套 coroutine，脚本通过 `fx.wait` 协作让出。
- 每次恢复最多10000条VM指令，强制让出超过20次停止；最多10000次恢复、50000次API调用。
- 源码上限64K字符、最多128张同时存在的图片，每个节点可配置0.1～120秒时限；异常和无限循环均清理对象。
- 这是限制功能和防止常见失控的**进程内解释器**，不是面对恶意输入的强安全隔离。没有硬堆内存配额，table/脚本分配仍可能消耗内存；只运行作者审阅的本地脚本，不加载网络或未知用户脚本。
- 尚不支持原生 Lua C 模块、任意 shader/material、任意游戏对象、视频或持久程序化资源；新的底层能力应由 C# 显式注册受控 API，而不是直接开放 CLR。

## 验证与依赖

- `工具 > 视觉小说 > 运行核心测试` 覆盖实际Lua执行、math/循环、暂停、无限循环抢占、受限globals、错误定位、非法wait、超时和清理。
- `工具 > 视觉小说 > 运行Lua演出播放测试` 运行示例，验证12个图像、射线安全、暂停、读档重播、推进和清理；报告 `.codely-cli/test-results/visual-novel-lua.txt`，截图 `visual-novel-lua.png`。
- 官方 DLL、许可证和来源信息在 `Assets/VisualNovel/ThirdParty/MoonSharp/`。
- 当前仅验证 Unity 6000.6 编辑器内编译和播放。遵守用户要求，不构建；IL2CPP/移动端/AOT/裁剪未验证，不能以编辑器测试代替平台兼容结论。
