using UnityEngine;

/// <summary>
/// 安全模块日志：正式包默认关闭常规输出，避免字符串成为逆向定位点。
/// 错误始终输出，方便真机排查。
/// </summary>
public static class 安全日志
{
    public static void 普通(string 内容)
    {
        if (安全配置.输出安全日志) Debug.Log("[安全] " + 内容);
    }

    public static void 警告(string 内容)
    {
        if (安全配置.输出安全日志) Debug.LogWarning("[安全] " + 内容);
    }

    public static void 错误(string 内容)
    {
        Debug.LogError("[安全] " + 内容);
    }
}
