# 打包与发布

## 入口

- `工具 > 项目 > 仅打包 Windows` → `Builds/Windows/Lament-the-Nameless.exe`（连同 `_Data`、`UnityPlayer.dll` 等，整目录约 171 MB，需整体分发）。
- `工具 > 项目 > 仅打包 Android` → `Builds/Android/Lament-the-Nameless.apk`（约 55 MB）。
- 回执：`Builds/build-receipt.json`（target/result/size/errors/warnings）。`Builds/` 已在 `.gitignore` 中。
- 构建脚本：`Assets/VisualNovel/Editor/ProjectBuilder.cs`。构建前必须退出 Play Mode。

## 签名状态

- 项目指向 `Keys/lament-release.keystore`（alias `lament`），但仓库内没有留存 keystore/keyalias 密码，`androidUseCustomKeystore` 被构建脚本回退为关闭，因此当前 APK 为 **Android Debug 签名**（`CN=Android Debug`），仅可安装试玩，不能上架。
- 正式签名：在 `Edit > Project Settings > Player > Android > Publishing Settings` 勾选 Custom Keystore 并填入两个密码后重新打包；不要把密码写进仓库。

## 构建环境修复记录（2026-11-28）

内置 NDK（r27c）是从 `android-ndk-r27c/` 目录搬入 `AndroidPlayer/NDK` 的，`toolchains/llvm/prebuilt/linux-x86_64/bin` 下 14 个符号链接仍指向已不存在的 `android-ndk-r27c/...`，导致 IL2CPP 报 `clang++: 没有那个文件或目录`。已把这些链接改为同目录相对链接（`clang++ -> clang`、`clang -> clang-18`、`ld -> ld.lld` 等），`clang++ --version` 验证通过。重新安装 Android 模块后若再出现，按同样方式修复即可。

## 实测耗时（本机 31 GB 内存 / 无 swap）

| 场景 | 耗时 | 说明 |
|---|---|---|
| Android 首次完整构建 | ≈13 分钟 | Gradle `assembleRelease` 单项 556 秒；冷 Gradle 缓存、首次 Android 资源导入、367 个 shader、IL2CPP Release |
| Android 无改动重建 | 67 秒 | IL2CPP 与 Gradle 任务均为 up-to-date，仅校验/搬运产物 |
| Windows 首次构建 | ≈4 分钟 | 含 408 变体 shader 编译（回执记录 3 个 FreeType 字体错误，属 LFS 指针问题） |

改脚本/资源后重建会重新执行 IL2CPP（数分钟级），但没有首次那 9 分钟 Gradle 依赖转换。项目内 `com.unity.ai.inference`（Sentis）与 `com.unity.ai.assistant` 带来大量 shader 与原生库，是 shader 数量与包体的主要来源；如需更快更小，可在确认 TJGenerators 不依赖 Sentis 后移除。

## 已知问题

- **黑屏（2026-11-28 已修复）**：目录英文化后，`Assets/Scenes/Login.unity` 的 `AnimSceneTransition.目标场景` 仍是旧名「游戏大厅」，且 Unity 在 YAML 中把中文存成 `\u6E38\u620F\u5927\u5385`，普通中文搜索查不到，设备日志报 `[AnimSceneTransition] 场景 "游戏大厅" 无法加载`。修复：`工具 > 项目 > 修复英文化后的场景引用` 会同时改写 `WindowDatabase.sceneName`、`NovelEntryPoint.runtimeScene` 与 `AnimSceneTransition.目标场景`。改名后请按 `\uXXXX` 形式全量搜索一次。


- `Assets/Art/*.png`、`Assets/Fonts/*.otf`、`Assets/TextMesh Pro/Fonts/LiberationSans.ttf` 在本工作区是 Git LFS 指针文件，导入会报 `could not be read` / `FreeType error`。不影响打包含中文的 TMP SDF 字体资产，但源图与源字体需 `git lfs pull` 才能恢复。
- 未在真机/Windows 实机验证运行，只验证了构建成功与编辑器内播放。
