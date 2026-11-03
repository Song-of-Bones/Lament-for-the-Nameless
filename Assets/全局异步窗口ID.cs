using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 全局窗口数据库：现在作为场景组件存在，方便直接在 Inspector 面板配置。
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

    [Header("窗口配置列表")]
    public List<WindowConfig> configs = new List<WindowConfig>();

    /// <summary>根据 ID 获取实际场景名</summary>
    public string GetSceneName(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        var config = configs.Find(c => c.id == id);
        return config != null ? config.sceneName : null;
    }
}