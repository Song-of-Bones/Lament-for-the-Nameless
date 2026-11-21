using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 加密资源的统一加载入口。
/// 资源放在 StreamingAssets/EncryptedAssets 下，由编辑器工具加密为 .bytes。
/// Android 上 StreamingAssets 位于 APK 内，必须使用协程版本（内部走 UnityWebRequest）。
/// 资源配置了完整性清单时，清单同时充当允许列表：不在清单中的路径会被拒绝。
/// </summary>
public static class 加密资源
{
    /// <summary>安全检查未通过时置为 true，所有加载请求都会被拒绝。</summary>
    public static bool 已禁用 { get; private set; }

    /// <summary>由安全核心在检测到异常时调用。</summary>
    internal static void 禁用()
    {
        已禁用 = true;
    }

    private static Dictionary<string, string> _清单;

    /// <summary>进入运行前清空状态，避免关闭域重载时沿用上一次会话的禁用标记。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void 重置状态()
    {
        已禁用 = false;
        _清单 = null;
    }

    public static string 目录路径
    {
        get { return Path.Combine(Application.streamingAssetsPath, 安全配置.资源目录名); }
    }

    internal static void 设置清单(Dictionary<string, string> 清单)
    {
        _清单 = 清单;
    }

    /// <summary>校验允许列表并解密。失败抛出异常，调用方按业务处理。</summary>
    public static byte[] 解密(byte[] 密文, string 相对路径 = null)
    {
        if (已禁用) throw new InvalidOperationException("加密资源已禁用（安全检查未通过）。");

        byte[] 密钥 = 密钥仓库.取主密钥();
        if (密钥 == null) throw new InvalidOperationException("缺少资源密钥。");

        if (安全配置.启用资源完整性校验)
        {
            if (string.IsNullOrEmpty(相对路径))
                throw new InvalidOperationException("启用完整性校验时必须提供资源相对路径。");

            if (!完整性校验.清单就绪)
                throw new InvalidOperationException("资源完整性清单尚未就绪，已拒绝加载：" + 相对路径);

            string 路径 = 规范(相对路径);
            var 清单 = 完整性校验.清单;
            if (清单 == null || !清单.ContainsKey(路径))
                throw new System.Security.Cryptography.CryptographicException("资源不在完整性清单中：" + 路径);
        }

        return 加密文件格式.解封(密文, 密钥);
    }

    /// <summary>解密并生成贴图。贴图会被标记为不可读，减少内存转储提取。</summary>
    public static Texture2D 加载图片(byte[] 密文, string 相对路径 = null)
    {
        byte[] 明文 = 解密(密文, 相对路径);
        try
        {
            var 贴图 = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!贴图.LoadImage(明文))
            {
                UnityEngine.Object.Destroy(贴图);
                throw new InvalidDataException("图片数据无法解析：" + 相对路径);
            }

            贴图.Apply(false, true); // 释放 CPU 端可读副本
            return 贴图;
        }
        finally
        {
            Array.Clear(明文, 0, 明文.Length);
        }
    }

    public static Sprite 加载精灵(byte[] 密文, string 相对路径 = null, float 像素每单位 = 100f)
    {
        Texture2D 贴图 = 加载图片(密文, 相对路径);
        var 精灵 = Sprite.Create(贴图, new Rect(0f, 0f, 贴图.width, 贴图.height), new Vector2(0.5f, 0.5f), 像素每单位);
        精灵.name = string.IsNullOrEmpty(相对路径) ? "加密图片" : 相对路径;
        return 精灵;
    }

    /// <summary>
    /// 解密并加载 AssetBundle。
    /// 解密后的数据先写入 temporaryCachePath（按密文哈希命名，可复用），再用 LoadFromFile 加载，
    /// 避免 LoadFromMemory 在内存中同时保留“托管明文 + 原生副本”。
    /// </summary>
    public static AssetBundle 加载资源包(byte[] 密文, string 相对路径 = null)
    {
        byte[] 明文 = 解密(密文, 相对路径);
        string 缓存文件;

        try
        {
            缓存文件 = 写入资源包缓存(明文, 缓存键(密文, 相对路径));
        }
        finally
        {
            Array.Clear(明文, 0, 明文.Length);
        }

        var 包 = AssetBundle.LoadFromFile(缓存文件);
        if (包 == null) throw new InvalidDataException("资源包解析失败：" + 相对路径);
        return 包;
    }

    /// <summary>清理旧的资源包缓存，建议在启动时调用一次。</summary>
    public static void 清理资源包缓存(int 保留天数 = 3)
    {
        try
        {
            string 目录 = 缓存目录;
            if (!Directory.Exists(目录)) return;

            DateTime 期限 = DateTime.UtcNow.AddDays(-Mathf.Max(0, 保留天数));
            string[] 文件 = Directory.GetFiles(目录, "*.bundle");
            for (int i = 0; i < 文件.Length; i++)
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(文件[i]) < 期限) File.Delete(文件[i]);
                }
                catch
                {
                    // 正在使用的文件删不掉，忽略
                }
            }
        }
        catch
        {
        }
    }

    // ------------------------------------------------------------------
    // 异步 / 协程接口
    // ------------------------------------------------------------------

    public static IEnumerator 读取文件协程(string 相对路径, Action<byte[]> 完成, Action<string> 失败 = null)
    {
        string 路径;
        try
        {
            路径 = 取得完整路径(相对路径);
        }
        catch (Exception e)
        {
            if (失败 != null) 失败(e.Message);
            yield break;
        }

        if (!路径.Contains("://"))
        {
            // 放到线程池读取，避免大文件在主线程造成卡顿
            var 结果 = new 读取结果();
            var 任务 = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    结果.数据 = File.ReadAllBytes(路径);
                }
                catch (Exception e)
                {
                    结果.错误 = e.Message;
                }
            });

            while (!任务.IsCompleted) yield return null;

            if (任务.IsFaulted)
            {
                if (失败 != null) 失败(任务.Exception != null ? 任务.Exception.GetBaseException().Message : "读取失败");
                yield break;
            }

            if (结果.数据 != null)
            {
                if (完成 != null) 完成(结果.数据);
            }
            else if (失败 != null)
            {
                失败(结果.错误 ?? "读取失败");
            }

            yield break;
        }

        using (var 请求 = UnityWebRequest.Get(路径))
        {
            yield return 请求.SendWebRequest();

            if (请求.result != UnityWebRequest.Result.Success)
            {
                if (失败 != null) 失败(请求.error);
            }
            else if (完成 != null)
            {
                完成(请求.downloadHandler.data);
            }
        }
    }

    public static IEnumerator 加载图片协程(string 相对路径, Action<Texture2D> 完成, Action<string> 失败 = null)
    {
        yield return 读取文件协程(相对路径, 数据 =>
        {
            try
            {
                if (完成 != null) 完成(加载图片(数据, 相对路径));
            }
            catch (Exception e)
            {
                if (失败 != null) 失败(e.Message);
            }
        }, 失败);
    }

    public static IEnumerator 加载资源包协程(string 相对路径, Action<AssetBundle> 完成, Action<string> 失败 = null)
    {
        yield return 读取文件协程(相对路径, 数据 =>
        {
            try
            {
                if (完成 != null) 完成(加载资源包(数据, 相对路径));
            }
            catch (Exception e)
            {
                if (失败 != null) 失败(e.Message);
            }
        }, 失败);
    }

    // ------------------------------------------------------------------

    internal static string 取得完整路径(string 相对路径)
    {
        string 路径 = 规范(相对路径);
        if (路径.Length == 0) throw new ArgumentException("资源路径为空。");
        if (路径.Contains("..")) throw new ArgumentException("非法的资源路径：" + 相对路径);

        string 根 = 目录路径;

        if (根.Contains("://"))
            return (根.TrimEnd('/') + "/" + 路径).Replace('\\', '/');

        string 根完整 = Path.GetFullPath(根);
        string 完整 = Path.GetFullPath(Path.Combine(根完整, 路径));

        if (!完整.StartsWith(根完整, StringComparison.OrdinalIgnoreCase) ||
            完整.Length == 根完整.Length)
            throw new ArgumentException("非法的资源路径：" + 相对路径);

        return 完整.Replace('\\', '/');
    }

    private static string 规范(string 路径)
    {
        return (路径 ?? string.Empty).Replace('\\', '/').TrimStart('/');
    }

    private static string 缓存目录
    {
        get { return Path.Combine(Application.temporaryCachePath, 安全配置.资源包缓存目录名); }
    }

    private static string 缓存键(byte[] 密文, string 相对路径)
    {
        if (_清单 != null && !string.IsNullOrEmpty(相对路径))
        {
            string 路径 = 规范(相对路径);
            string 哈希;
            if (_清单.TryGetValue(路径, out 哈希) && !string.IsNullOrEmpty(哈希)) return 哈希;
        }

        return 加密文件格式.摘要十六进制(密文);
    }

    private static string 写入资源包缓存(byte[] 明文, string 键)
    {
        string 目录 = 缓存目录;
        Directory.CreateDirectory(目录);

        string 路径 = Path.Combine(目录, 键 + ".bundle");
        if (!File.Exists(路径) || new FileInfo(路径).Length != 明文.Length)
            File.WriteAllBytes(路径, 明文);

        return 路径;
    }

    private class 读取结果
    {
        public byte[] 数据;
        public string 错误;
    }
}
