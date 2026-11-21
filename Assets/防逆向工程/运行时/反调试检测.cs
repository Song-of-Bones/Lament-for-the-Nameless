using System;
using System.IO;
using System.Threading;
using UnityEngine;

/// <summary>
/// 反注入 / 反调试检测（后台线程运行，避免卡顿）。
/// Android：逐行扫描 /proc/self/maps 中的 Frida、Xposed、Zygisk 等特征（命中即返回，不整份读入）；
/// 读取 /proc/self/status 的 TracerPid；探测 Frida 默认端口 27042。
/// 全平台：检测托管调试器是否附着（仅非编辑器构建）。
/// 注意：这些手段只能提高门槛，无法阻止已 root 设备上的深度 Hook。
/// </summary>
public static class 反调试检测
{
    private static volatile bool _发现异常;
    private static volatile string _详情 = string.Empty;

#if !UNITY_EDITOR
    private static Thread _线程;
#endif

    public static bool 发现异常
    {
        get { return _发现异常; }
    }

    public static string 详情
    {
        get { return _详情; }
    }

    /// <summary>进入运行前清空检测状态，避免关闭域重载时沿用上一次结果。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void 重置()
    {
        _发现异常 = false;
        _详情 = string.Empty;
    }

    /// <summary>启动一次后台检测线程（重复调用无副作用）。</summary>
    public static void 启动()
    {
#if UNITY_EDITOR
        return; // 编辑器内不启动常驻线程，避免反复进入播放模式累积线程
#else
        if (_线程 != null) return;

        _线程 = new Thread(循环)
        {
            IsBackground = true,
            Name = "SecWatch"
        };
        _线程.Start();
#endif
    }

    private static void 循环()
    {
        int 间隔 = Math.Max(5, 安全配置.反调试检测间隔秒) * 1000;

        while (true)
        {
            try
            {
                string 原因 = 检测一次();
                if (!string.IsNullOrEmpty(原因))
                {
                    _详情 = 原因;
                    _发现异常 = true;
                    return;
                }
            }
            catch
            {
                // 检测本身不允许影响游戏运行
            }

            Thread.Sleep(间隔);
        }
    }

    /// <summary>同步检测一次，返回原因；正常返回 null。</summary>
    public static string 检测一次()
    {
#if !UNITY_EDITOR
        if (System.Diagnostics.Debugger.IsAttached)
            return "检测到托管调试器连接。";
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
        string 特征 = 扫描注入特征();
        if (特征 != null)
            return "检测到注入框架特征：" + 特征;

        string 状态 = 读取文本("/proc/self/status");
        if (状态 != null)
        {
            string[] 行 = 状态.Split('\n');
            for (int i = 0; i < 行.Length; i++)
            {
                if (!行[i].StartsWith("TracerPid:", StringComparison.Ordinal)) continue;

                int pid;
                if (int.TryParse(行[i].Substring(10).Trim(), out pid) && pid > 0)
                    return "检测到调试器附着（TracerPid=" + pid + "）。";
            }
        }

        if (Frida端口开放())
            return "检测到 Frida 默认端口服务（27042）。";
#endif

        return null;
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private static readonly string[] 注入特征 =
    {
        "frida-agent", "frida-gadget", "frida-server", "libfrida",
        "libgadget.so", "xposed", "edxp", "substrate", "zygisk", "lsposed", "riru"
    };

    /// <summary>逐行扫描 maps，命中即返回，避免整份读入造成大字符串分配。</summary>
    private static string 扫描注入特征()
    {
        try
        {
            using (var 流 = new FileStream("/proc/self/maps", FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var 读 = new StreamReader(流))
            {
                string 行;
                while ((行 = 读.ReadLine()) != null)
                {
                    for (int i = 0; i < 注入特征.Length; i++)
                    {
                        if (行.IndexOf(注入特征[i], StringComparison.OrdinalIgnoreCase) >= 0)
                            return 注入特征[i];
                    }
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static string 读取文本(string 路径)
    {
        try
        {
            return File.ReadAllText(路径);
        }
        catch
        {
            return null;
        }
    }

    private static bool Frida端口开放()
    {
        try
        {
            using (var 客户端 = new System.Net.Sockets.TcpClient())
            {
                var 任务 = 客户端.ConnectAsync("127.0.0.1", 27042);
                return 任务.Wait(250) && 客户端.Connected;
            }
        }
        catch
        {
            return false;
        }
    }
#endif
}
