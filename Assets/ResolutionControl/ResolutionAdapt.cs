using UnityEngine;

/// <summary>
/// 「分辨率自适应」开关的全局状态：默认开启，写入 PlayerPrefs，变更时通知 BackgroundFit。
/// 只负责背景图，不影响其它 UI。
/// </summary>
public static class ResolutionAdapt
{
    private const string 键 = "resolution_adapt";

    private static bool _enabled = true;
    private static bool _loaded;

    /// <summary>设置变更事件（参数为新的开关状态）。</summary>
    public static event System.Action<bool> 改变;

    public static bool 已开启
    {
        get
        {
            if (!_loaded) 读取();
            return _enabled;
        }
        set
        {
            if (!_loaded) 读取();
            if (_enabled == value) return;

            _enabled = value;
            PlayerPrefs.SetInt(键, value ? 1 : 0);
            PlayerPrefs.Save();
            改变?.Invoke(_enabled);
        }
    }

    private static void 读取()
    {
        _loaded = true;
        _enabled = PlayerPrefs.GetInt(键, 1) == 1;
    }

    /// <summary>进入运行前重置静态缓存，避免关闭域重载时沿用上一次的状态。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void 重置()
    {
        _loaded = false;
        _enabled = true;
        改变 = null;
    }
}
