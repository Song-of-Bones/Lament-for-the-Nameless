using System.Text;
using UnityEngine;

/// <summary>
/// 本机设备密钥：Android 读取 ANDROID_ID，PC/编辑器使用系统硬件唯一 ID（Windows 上即 CHID 派生的唯一标识），
/// 再经过带固定盐的 64 位散列与雪崩混合，映射成 6 位数字/大写字母。
/// 同一台设备每次得到的结果一致。
/// </summary>
public static class DeviceKey
{
    private const string 盐 = "LAMENT-FOR-THE-NAMELESS|DEVICE-KEY|v1";

    // 去掉容易混淆的 I / O
    private const string 字母表 = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    private static string _cached;

    public static string 获取()
    {
        if (string.IsNullOrEmpty(_cached)) _cached = 生成(获取原始ID());
        return _cached;
    }

    /// <summary>读取设备原始唯一 ID。</summary>
    public static string 获取原始ID()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
            using (var secure = new AndroidJavaClass("android.provider.Settings$Secure"))
            {
                var id = secure.CallStatic<string>("getString", resolver, "android_id");
                if (!string.IsNullOrEmpty(id)) return id;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[DeviceKey] 读取 Android ID 失败，回退到系统唯一 ID：" + e.Message);
        }
#endif
        // PC / 编辑器：SystemInfo.deviceUniqueIdentifier 在 Windows 上由硬件信息（CHID）派生
        return SystemInfo.deviceUniqueIdentifier;
    }

    /// <summary>把原始 ID 生成为 6 位密钥。</summary>
    public static string 生成(string 原始ID)
    {
        if (string.IsNullOrEmpty(原始ID)) 原始ID = "unknown";

        // FNV-1a 64 位
        ulong h = 1469598103934665603UL;
        var bytes = Encoding.UTF8.GetBytes(盐 + "|" + 原始ID);
        for (int i = 0; i < bytes.Length; i++)
        {
            h ^= bytes[i];
            h *= 1099511628211UL;
        }

        // MurmurHash3 finalizer 雪崩混合
        h ^= h >> 33;
        h *= 0xff51afd7ed558ccdUL;
        h ^= h >> 33;
        h *= 0xc4ceb9fe1a85ec53UL;
        h ^= h >> 33;

        var sb = new StringBuilder(6);
        for (int i = 0; i < 6; i++)
        {
            sb.Append(字母表[(int)(h % (ulong)字母表.Length)]);
            h /= (ulong)字母表.Length;
        }

        return sb.ToString();
    }

    /// <summary>把 "123ABC" 格式化成 "1 2 3 A B C"。</summary>
    public static string 格式化(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;

        var sb = new StringBuilder(key.Length * 2);
        for (int i = 0; i < key.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(key[i]);
        }
        return sb.ToString();
    }
}
