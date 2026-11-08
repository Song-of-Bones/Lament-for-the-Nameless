using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 全局窗口数据库：现在作为场景组件存在，方便直接在 Inspector 面板配置。
/// 异步窗口与跳转窗口各有一份列表，ID 可以重复，互不影响。
/// </summary>
public class WindowDatabase : MonoBehaviour
{
    [System.Serializable]
    public class WindowConfig
    {
        [Tooltip("给按钮调用的全局 ID，比如 setting_audio")]
        public string id;
        [Tooltip("实际要加载的场景名，必须包含在 Build Settings 里")]
        public string sceneName;
    }

    [Header("异步窗口配置列表")]
    [Tooltip("按钮「窗口是否为跳转」不勾选时，加在本场景之上的窗口")]
    public List<WindowConfig> configs = new List<WindowConfig>();

    [Header("跳转窗口配置列表")]
    [Tooltip("按钮「窗口是否为跳转」勾选时，直接切换到的场景；ID 可与上面的列表相同")]
    public List<WindowConfig> jumpConfigs = new List<WindowConfig>();

    /// <summary>兼容旧调用：按异步窗口列表查找。</summary>
    public string GetSceneName(string id) => GetAsyncSceneName(id);

    /// <summary>按异步窗口列表查找场景名。</summary>
    public string GetAsyncSceneName(string id)
    {
        return 查找(configs, id);
    }

    /// <summary>按跳转窗口列表查找场景名。</summary>
    public string GetJumpSceneName(string id)
    {
        return 查找(jumpConfigs, id);
    }

    private static string 查找(List<WindowConfig> 列表, string id)
    {
        if (string.IsNullOrEmpty(id) || 列表 == null) return null;

        var config = 列表.Find(c => c != null && c.id == id);
        return config != null ? config.sceneName : null;
    }
}
