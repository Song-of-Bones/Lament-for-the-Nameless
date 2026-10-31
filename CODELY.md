

## Codely Structured Memories

### User

### Feedback
- [2026-10-31 20:31:02] Unity Animator 无法驱动 TMP 的 m_maxVisibleCharacters（int 字段，float 曲线被忽略，运行值恒为 99999），不要用动画曲线做打字机；改用 RectMask2D 宽度动画做横向擦除，或拆分单字物体 + CanvasGroup alpha（本项目 2026-10-31 已如此实现）。TMP characterSpacing 以 1/100 em 计，55 ≈ 0.55em。

### Project
- [2026-10-31 20:30:57] 2026-10-31：登录场景 Assets/Scenes/登录场景.unity 的标题卡由 Animator 驱动：动画1组件下 骸歌（512x256 实心白色字形 RawImage）与 白条（白色竖条+子物体黑色遮罩，推入时挡住标题右半边直到约 3.1s，属既有擦除设计）。新增 文字信息 组（细线-上、中文副标题 RectMask2D 擦除+光边、英文副标题、细线-下），由 Assets/动画/文字信息.anim + .controller 控制；总时长已从 5.83s 延至 7.6s（骸歌.anim 淡出帧改为 7.35/7.60）。场景内另有一个空的 TextReveal 根物体，每次播放报“未指定文本组件”警告。
### Reference

