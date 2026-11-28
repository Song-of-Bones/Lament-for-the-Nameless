# 登录开场动画与场景流程（SceneFlow）

> 适用范围：`Assets/Scenes/Login.unity`（Build #0）以及所有"播完过场后要离开本场景"的流程。
> 设计原则：**场景只负责播自己的动画，跳到哪里由管理器按配置决定**；这条配置在场景连接图里可直接看到和修改。

---

## 1. 登录场景结构

`Assets/Scenes/Login.unity` 的根对象：

| 根对象 | 作用 |
|---|---|
| `Main Camera` | 相机 + Canvas：`Canvas/动画1组件/…` |
| `EventSystem` | UI 事件系统 |
| `AnimSceneTransition` | 过场出口：`AnimSceneTransition` + `SceneFlowManager` + `WindowDatabase` |

`动画1组件` 下四个**不循环**的 Animator 各驱动一组画面（时间轴按 `故障` 对齐 8.47s）：

| 对象 | 剪辑 | 时长 | 内容 |
|---|---|---|---|
| `骸歌` | `Haige.anim` | 8.470s | 512×256 白色字形标题 |
| `白条` | `WhiteBar.anim` | 5.833s | 白色竖条 + 子物体遮罩，推入擦除标题右半 |
| `文字信息` | `TextInfo.anim` | 8.470s | 细线 / 中文副标题（RectMask2D 擦除 + 光边）/ 英文副标题 |
| `故障层` | `Glitch.anim` | 8.470s | 故障偏色副本、雪花、闪白；`GlitchRamp` 读 `m_GlitchAmount` 做加速 |

时间轴：4.6s 构图完成 → 停到 6.6s → 6.6–8.4s 故障加速 → 8.42–8.455s 整屏白闪 → **8.47s 全部元素硬切为 0（画面自黑）**。

---

## 2. 跳转是怎么接的

```
AnimSceneTransition（场景里，只持流程 ID）
    │  播完动画
    ▼
SceneFlowManager.Play("login_intro")
    │  查本场景 WindowDatabase.jumpConfigs
    ▼
login_intro → Lobby   ←── 场景连接图里就是这条边（开场动画 [跳转] → Lobby）
    │
    ▼
SceneManager.LoadSceneAsync("Lobby", Single)
```

### 组件职责

- **`AnimSceneTransition`**（`Assets/Animation/Scripts/SceneTransitionEffect.cs`）
  只负责"等到动画结束"，然后发出流程 ID。字段：
  - `交给流程管理器`（默认开）：开启后**不再**读 `目标场景`；
  - `流程ID`：对应 `WindowDatabase` 跳转表里的 id；
  - `目标场景`：仅当关闭"交给流程管理器"时使用的旧路径，保持向后兼容。
- **`SceneFlowManager`**（`Assets/Animation/Scripts/SceneFlowManager.cs`）
  场景内组件，`Awake` 自动找本场景的 `WindowDatabase`：
  - `Resolve(id)`：id → 场景名；
  - `Preload(id)`：只加载不激活（切场景不卡）；
  - `Play(id)`：激活/加载目标场景（Single）。
- **`WindowDatabase`**（`Assets/WindowDatabase.cs`）
  `jumpConfigs` 就是流程跳转表（id → 场景名），与按钮用的 `configs` 互不影响。

### 为什么是「直出直入」

动画最后一帧已经把整屏切黑，而 `Lobby` 的 `LobbyIntro` 首帧是**全黑遮罩 + 0.15s 停留再淡出**。所以这里不需要额外的淡出淡入，直接切场景反而最干净；再加一层渐变只会在黑场之间多叠一次。

---

## 3. 动画完成判定（不改动画）

`AnimSceneTransition` 不碰 `.anim` / `.controller` / Animator 参数，也不用 Playable 覆盖，判定顺序：

1. **时长为主**：读一次当前状态的 `state.length`（`故障` = 8.47s），起算点用 `normalizedTime × length` 对齐，逐帧累加 `Time.deltaTime`（与 Animator 同步受 `timeScale` 影响）；
2. **Animator 播完为辅**：`!IsInTransition && normalizedTime >= 0.999` 也立刻放行；
3. **`最多等待` 兜底**（默认 0 = 不限）：动画被冻结时到点强制转场，不会卡死在登录页。

其他相关字段：

| 字段 | 默认 | 说明 |
|---|---|---|
| `提前预加载` | 开 | 动画开始 0.5s 后在后台 `LoadSceneAsync(allowSceneActivation=false)`，播完瞬间激活 |
| `允许跳过` | 开 | 允许点击/按键跳过 |
| `跳过需等待` | 1.5s | 开场阶段不响应跳过，避免误触打断动画 |

---

## 4. 在场景连接图里维护

入口：**工具 → 项目 → 场景连接图**（`Ctrl+Shift+G`）。

- `Login` 卡片会出现一行 `开场动画 [跳转] → Lobby`，以及一条橙色（跳转）连线指向 `Lobby`；
- 把这条线拖到别的场景卡片，会写回两处：`AnimSceneTransition.流程ID` 与本场景 `WindowDatabase.jumpConfigs`；
- 右键连线可自定义颜色（灰/蓝/橙/绿/紫 分别表示未连接/异步/跳转/直接/事件）；
- 卡片右侧的行是"出口"，卡片左侧圆形是"入口"；同一张卡的入口可以接多条线。

### 新增一个"播完就跳"的出口

1. 场景里放 `AnimSceneTransition`（或已有）+ `SceneFlowManager` + `WindowDatabase`；
2. `AnimSceneTransition.交给流程管理器` 打勾，`流程ID` 留空；
3. 在场景连接图里从该行拖线到目标场景 —— 流程 ID 与数据库条目会自动生成。

### 换目标场景

直接在场景连接图里把线拖到新场景；或手工改 `WindowDatabase.jumpConfigs` 里 `login_intro` 的 `sceneName`。

---

## 5. 排查

| 现象 | 原因 / 处理 |
|---|---|
| 动画播完停在登录页 | `SceneFlowManager` 不在场景里，或 `jumpConfigs` 没有该 id（Console 会有对应 warning） |
| 一开场就跳走 | `允许跳过` 开着且开场被误触；可关掉或加大 `跳过需等待` |
| 跳到别的场景 | `jumpConfigs` 里同 id 的另一条配置更靠前（列表从上往下取第一条） |
| 场景连接图里没有这条边 | `流程ID` 为空，或本场景没有 `WindowDatabase` |
