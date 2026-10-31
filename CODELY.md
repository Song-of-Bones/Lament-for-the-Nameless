

## Codely Structured Memories

### User

### Feedback
- [2026-10-31 20:31:02] Unity Animator 无法驱动 TMP 的 m_maxVisibleCharacters（int 字段，float 曲线被忽略，运行值恒为 99999），不要用动画曲线做打字机；改用 RectMask2D 宽度动画做横向擦除，或拆分单字物体 + CanvasGroup alpha（本项目 2026-10-31 已如此实现）。TMP characterSpacing 以 1/100 em 计，55 ≈ 0.55em。

### Project
- [2026-11-01 01:48:20] 2026-10-31：登录场景 Assets/Scenes/登录场景.unity 的标题卡由 Animator 驱动：动画1组件下 骸歌（512x256 白色字形 RawImage）与 白条（白色竖条+子物体黑色遮罩，推入时挡住标题右半边直到约 3.1s，属既有擦除设计）。文字信息 组（细线-上、中文副标题 RectMask2D 擦除+光边、英文副标题、细线-下）由 Assets/动画/文字信息.anim 控制；尾段 故障层 由 Assets/动画/故障.anim + GlitchRamp.cs 控制：GlitchRamp 读取 Animator 动画的 m_GlitchAmount，幅度按 k、频率按 k² 加速（RGB 步进 8→70/s，Shift 0.004→0.03，撕裂率 0.01→0.45），并创建材质实例写入 UI/RGBGlitch、UI/SnowNoise 的 _Phase（着色器已不再用 _Time）。故障层含 标题_故障、中文_红/蓝 与 英文_红/蓝 偏色副本（±8px）、雪花（每格独立随机频率）、闪白。总时长 8.47s：4.6s 构图完成停留到 6.6s → 6.6–8.4s 故障加速 → 8.42–8.455s 整屏白闪 → 8.47s 标题/文字/雪花同时硬切为 0。场景内另有一个空的 TextReveal 根物体，每次播放报“未指定文本组件”警告。



### Reference

