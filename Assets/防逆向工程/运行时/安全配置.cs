using UnityEngine;

/// <summary>
/// 防逆向总配置。常量会编译进程序集，修改后必须重新构建才生效。
/// 建议在构建服务器上通过脚本替换这些值，或改用后端下发的配置。
/// </summary>
public static class 安全配置
{
    // ------------------------------------------------------------------
    // 安装包校验
    // ------------------------------------------------------------------

    /// <summary>
    /// 期望的 APK 签名证书 SHA-256（小写十六进制、去掉冒号）。
    /// 留空则不校验。获取方式：keytool -printcert -jarfile 你的.apk
    /// </summary>
    public static readonly string 期望APK签名 = "e0aa0dc820bcaccb2b271ae058673e020172f0d2895168c9c0dea5aebe8f3147";

    /// <summary>使用 Unity 内置的 Application.genuine 正版校验（仅 Android / iOS 生效）。</summary>
    public static readonly bool 启用正版校验 = true;

    // ------------------------------------------------------------------
    // 运行时检测
    // ------------------------------------------------------------------

    /// <summary>后台检测 Frida、Xposed、调试器等注入与调试环境。</summary>
    public static readonly bool 启用反调试 = true;

    /// <summary>启动时载入加密资源清单，并在每次加载资源前核对允许列表。</summary>
    public static readonly bool 启用资源完整性校验 = true;

    /// <summary>
    /// 清单缺失或无法解密时是否视为篡改。
    /// 关闭后会在缺少清单时跳过校验（旧行为，容易被“删清单”绕过，不建议）。
    /// </summary>
    public static readonly bool 清单缺失视为篡改 = true;

    /// <summary>0 = 只记录日志；1 = 禁用加密资源加载；2 = 退出游戏。</summary>
    public static readonly int 响应方式 = 1;

    /// <summary>启动后延迟多久开始首轮安装包检测（秒），避免影响开场加载。</summary>
    public static readonly float 首次检测延迟 = 1.5f;

    /// <summary>运行期重复检测的间隔（秒）。</summary>
    public static readonly float 检测间隔 = 20f;

    /// <summary>后台注入检测线程的轮询间隔（秒）。</summary>
    public static readonly int 反调试检测间隔秒 = 15;

    // ------------------------------------------------------------------
    // 资源约定
    // ------------------------------------------------------------------

    /// <summary>StreamingAssets 下的加密资源目录名。使用 ASCII，避免 Android 上 URL 转义问题。</summary>
    public static readonly string 资源目录名 = "EncryptedAssets";

    /// <summary>完整性清单文件名（由编辑器工具生成）。</summary>
    public static readonly string 清单文件名 = "manifest.bytes";

    /// <summary>解密后的 AssetBundle 缓存目录（位于 temporaryCachePath 下）。</summary>
    public static readonly string 资源包缓存目录名 = "EncryptedBundleCache";

    // ------------------------------------------------------------------
    // 日志与构建
    // ------------------------------------------------------------------

    /// <summary>是否输出安全模块的常规日志；正式包默认关闭，避免给逆向者留下字符串线索。</summary>
    public static readonly bool 输出安全日志 = 计算日志开关();

    private static bool 计算日志开关()
    {
#if UNITY_EDITOR
        return true;
#else
        return Debug.isDebugBuild;
#endif
    }

    /// <summary>构建时发现未生成密钥 / 未使用 IL2CPP 是否直接失败（CI 建议开启）。</summary>
    public static readonly bool 构建严格检查 = true;
}
