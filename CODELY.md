

## Codely Structured Memories

### User

### Feedback
- [2026-10-31 20:31:02] Unity Animator 无法驱动 TMP 的 m_maxVisibleCharacters（int 字段，float 曲线被忽略，运行值恒为 99999），不要用动画曲线做打字机；改用 RectMask2D 宽度动画做横向擦除，或拆分单字物体 + CanvasGroup alpha（本项目 2026-10-31 已如此实现）。TMP characterSpacing 以 1/100 em 计，55 ≈ 0.55em。

### Project
- [2026-11-01 01:48:20] 2026-10-31：登录场景 Assets/Scenes/登录场景.unity 的标题卡由 Animator 驱动：动画1组件下 骸歌（512x256 白色字形 RawImage）与 白条（白色竖条+子物体黑色遮罩，推入时挡住标题右半边直到约 3.1s，属既有擦除设计）。文字信息 组（细线-上、中文副标题 RectMask2D 擦除+光边、英文副标题、细线-下）由 Assets/动画/文字信息.anim 控制；尾段 故障层 由 Assets/动画/故障.anim + GlitchRamp.cs 控制：GlitchRamp 读取 Animator 动画的 m_GlitchAmount，幅度按 k、频率按 k² 加速（RGB 步进 8→70/s，Shift 0.004→0.03，撕裂率 0.01→0.45），并创建材质实例写入 UI/RGBGlitch、UI/SnowNoise 的 _Phase（着色器已不再用 _Time）。故障层含 标题_故障、中文_红/蓝 与 英文_红/蓝 偏色副本（±8px）、雪花（每格独立随机频率）、闪白。总时长 8.47s：4.6s 构图完成停留到 6.6s → 6.6–8.4s 故障加速 → 8.42–8.455s 整屏白闪 → 8.47s 标题/文字/雪花同时硬切为 0。场景内另有一个空的 TextReveal 根物体，每次播放报“未指定文本组件”警告。
- [2026-11-09 06:35:52] 2026-11-09：Assets/动画/脚本 下脚本被重命名为中文文件名，类名未变：LobbyIntro→切场过渡动画控制器.cs、AnimSceneTransition→动画场景效果器.cs、TextReveal→文字效果器.cs、UIMenuButton→游戏主页_4按钮动画控制器.cs、UIMenuPopIn→缩放适配控制器.cs、GlitchRamp→雪花效果控制器.cs。改代码前先按类名定位文件，不要按英文文件名猜路径。
- [2026-11-09 06:36:20] 2026-11-09：设置窗口架构：Assets/全局异步窗口ID.cs 的 WindowDatabase 有两份列表（configs 异步、jumpConfigs 跳转，ID 可重复），SettingsWindowManager（动画/脚本/异步窗口管理.cs）的 打开设置(id, bool 是跳转)：false 走 Additive 加载并播放 SettingsWindow 开窗动画，true 走 LoadSceneAsync Single 跳转；UIMenuButton（游戏主页_4按钮动画控制器.cs）有 打开窗口ID 与 窗口是否为跳转，确认时调用。设置场景 设置_异步窗口 已加入 Build Settings。分辨率适配模块在 Assets/分辨率控制器：ResolutionAdapt（PlayerPrefs 键 resolution_adapt，默认开）+ BackgroundFit（参考 2400×1080 做 cover，父节点需 背景裁切 的 RectMask2D 防止溢出显示）。
- [2026-11-09 06:36:50] 2026-11-09：Assets/防逆向工程 模块结构：运行时/安全核心、密钥仓库（主密钥 XOR 掩码拆分）、加密文件格式（LFLN = AES-256-CBC + HMAC-SHA256，头 56 字节）、加密资源、完整性校验、反调试检测；Editor/密钥生成器、资源加密工具、构建后处理。已知待改进（P0）：清单缺失/条目缺失时 fail-open、安全核心与 加密资源.已禁用 为 static 且未在 SubsystemRegistration 重置、清单逐文件 SHA-256 与 HMAC 重复、APK 签名用已废弃 GET_SIGNATURES、解密时 Clone 整个文件算 HMAC、LoadFromMemory 使内存翻倍且明文未清零。
- [2026-11-09 06:37:15] 2026-11-09：本项目圆角 UI 精灵（Assets/美术素材/圆角按钮.png、圆角面板.png、圆角卡片.png、圆角卡片描边.png）由 SDF 程序化生成，形状必须向纹理内部留 4~6px inset 再设置九宫格 border（inset+radius），否则抗锯齿/泛光边会被纹理边界裁掉，出现“边缘被切掉一像素”的观感。设置面板最终样式：接近不透明的黑底 0.02/0.022/0.028 @0.97，1px 白色描边面板，设备密钥固定在屏幕底部而非面板内。

### Reference

