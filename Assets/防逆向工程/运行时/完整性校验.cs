using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>
/// 安装包与资源完整性校验。
/// Android：Application.genuine + APK 签名证书 SHA-256（优先使用 API 28+ 的签名信息）；
/// 全平台：加密资源清单（清单本身经过 HMAC，无法在不知道密钥的情况下伪造）。
/// 清单同时充当允许列表：不在清单中的资源会被拒绝加载。
/// </summary>
public static class 完整性校验
{
    private static Dictionary<string, string> _清单;

    public static Dictionary<string, string> 清单
    {
        get { return _清单; }
    }

    /// <summary>清单是否已经载入完成（启用完整性校验时，资源加载会等待它为 true）。</summary>
    public static bool 清单就绪 { get; private set; }

    /// <summary>Unity 内置正版校验，仅 Android / iOS 可用。</summary>
    public static bool 校验正版(out string 原因)
    {
        原因 = null;

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        if (安全配置.启用正版校验 && Application.genuineCheckAvailable && !Application.genuine)
        {
            原因 = "应用安装包已被修改（Application.genuine 校验失败）。";
            return false;
        }
#endif
        return true;
    }

    /// <summary>校验当前 APK 的签名证书 SHA-256 是否与配置一致。</summary>
    public static bool 校验安卓签名(out string 实测)
    {
        实测 = null;

#if UNITY_ANDROID && !UNITY_EDITOR
        if (string.IsNullOrEmpty(安全配置.期望APK签名)) return true;

        try
        {
            using (var 玩家类 = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var 活动 = 玩家类.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var 包管理器 = 活动.Call<AndroidJavaObject>("getPackageManager"))
            {
                string 包名 = 活动.Call<string>("getPackageName");
                byte[] 证书 = 读取签名证书(包管理器, 包名);
                if (证书 == null)
                {
                    实测 = "未找到签名";
                    return false;
                }

                using (var sha = SHA256.Create())
                    实测 = 十六进制(sha.ComputeHash(证书));

                string 期望 = 安全配置.期望APK签名
                    .Replace(":", string.Empty)
                    .Replace(" ", string.Empty)
                    .Trim()
                    .ToLowerInvariant();

                return string.Equals(实测, 期望, StringComparison.Ordinal);
            }
        }
        catch (Exception e)
        {
            实测 = "读取签名失败：" + e.Message;
            return false;
        }
#else
        return true;
#endif
    }

    /// <summary>
    /// 载入加密资源清单。清单缺失或无法解密时，按 安全配置.清单缺失视为篡改 处理。
    /// </summary>
    public static IEnumerator 载入资源清单(Action<bool, string> 完成)
    {
        if (!安全配置.启用资源完整性校验)
        {
            清单就绪 = true;
            if (完成 != null) 完成(true, null);
            yield break;
        }

        yield return 加密资源.读取文件协程(安全配置.清单文件名,
            数据 =>
            {
                try
                {
                    byte[] 明文 = 加密文件格式.解封(数据, 密钥仓库.取主密钥());
                    _清单 = 解析清单(Encoding.UTF8.GetString(明文));
                    加密资源.设置清单(_清单);
                    清单就绪 = true;
                    安全日志.普通("资源完整性清单已载入，共 " + _清单.Count + " 项。");
                    if (完成 != null) 完成(true, null);
                }
                catch (Exception e)
                {
                    清单就绪 = false;
                    if (完成 != null) 完成(false, "资源清单校验失败：" + e.Message);
                }
            },
            错误 =>
            {
                清单就绪 = !安全配置.清单缺失视为篡改;
                if (安全配置.清单缺失视为篡改)
                {
                    if (完成 != null) 完成(false, "缺少资源完整性清单（" + 错误 + "），已按篡改处理。");
                }
                else
                {
                    安全日志.警告("未找到资源完整性清单（" + 错误 + "），本次运行跳过校验。");
                    if (完成 != null) 完成(true, null);
                }
            });
    }

    /// <summary>清空清单缓存（进入运行时调用）。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void 重置()
    {
        _清单 = null;
        清单就绪 = false;
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    /// <summary>优先 API 28+ 的签名信息，失败时回退到旧的 signatures 字段。</summary>
    private static byte[] 读取签名证书(AndroidJavaObject 包管理器, string 包名)
    {
        const int 获取签名证书 = 0x08000000; // PackageManager.GET_SIGNING_CERTIFICATES

        try
        {
            using (var 信息 = 包管理器.Call<AndroidJavaObject>("getPackageInfo", 包名, 获取签名证书))
            {
                if (信息 != null)
                {
                    using (var 签名信息 = 信息.Get<AndroidJavaObject>("signingInfo"))
                    {
                        if (签名信息 != null)
                        {
                            var 签名者 = 签名信息.Call<AndroidJavaObject[]>("getApkContentsSigners");
                            if (签名者 != null && 签名者.Length > 0)
                                return 签名者[0].Call<byte[]>("toByteArray");
                        }
                    }
                }
            }
        }
        catch (Exception)
        {
            // API < 28 或厂商定制，继续走旧接口
        }

        try
        {
            using (var 信息 = 包管理器.Call<AndroidJavaObject>("getPackageInfo", 包名, 64)) // GET_SIGNATURES
            {
                var 签名数组 = 信息.Get<AndroidJavaObject[]>("signatures");
                if (签名数组 != null && 签名数组.Length > 0)
                    return 签名数组[0].Call<byte[]>("toByteArray");
            }
        }
        catch (Exception)
        {
        }

        return null;
    }
#endif

    private static Dictionary<string, string> 解析清单(string 文本)
    {
        var 结果 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string[] 行 = 文本.Split('\n');

        for (int i = 0; i < 行.Length; i++)
        {
            string 内容 = 行[i].Trim();
            if (内容.Length == 0) continue;

            int 空格 = 内容.IndexOf(' ');
            if (空格 <= 0) continue;

            string 哈希 = 内容.Substring(0, 空格).Trim();
            string 路径 = 内容.Substring(空格 + 1).Trim().Replace('\\', '/');

            if (哈希.Length == 64 && 路径.Length > 0) 结果[路径] = 哈希;
        }

        return 结果;
    }

    private static string 十六进制(byte[] 数据)
    {
        var sb = new StringBuilder(数据.Length * 2);
        for (int i = 0; i < 数据.Length; i++) sb.Append(数据[i].ToString("x2"));
        return sb.ToString();
    }
}
