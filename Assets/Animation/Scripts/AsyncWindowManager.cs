using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 设置窗口管理器：按需异步加载窗口场景（Additive），支持通过 ID 查表动态加载不同窗口。
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("场景/设置窗口管理 (SettingsWindowManager)")]
public class SettingsWindowManager : MonoBehaviour
{
    [Header("全局配置")]
    [Tooltip("如果不手动指定，会自动在场景中寻找 WindowDatabase")]
    [SerializeField] private WindowDatabase 窗口数据库;
    
    [SerializeField] private bool 关闭后卸载场景 = true;

    private bool _loading;
    private bool _open;
    private SettingsWindow _window;
    private Scene _windowScene;
    private string _当前加载的场景名;

    public bool 已打开 => _open;
    public bool 加载中 => _loading;

    private void Awake()
    {
        // 如果没有手动赋值，自动在场景里找一下
        if (窗口数据库 == null)
        {
            窗口数据库 = FindAnyObjectByType<WindowDatabase>();
        }
    }

    /// <summary>根据全局注册的 ID 异步加载并打开窗口（默认按异步窗口处理）</summary>
    public void 打开设置(string 窗口ID)
    {
        打开设置(窗口ID, false);
    }

    /// <summary>根据全局注册的 ID 打开窗口；是跳转时直接切换场景，否则叠加加载为窗口</summary>
    public void 打开设置(string 窗口ID, bool 是跳转)
    {
        if (_open || _loading) return;

        if (窗口数据库 == null)
        {
            Debug.LogError("[SettingsWindowManager] 场景中找不到 WindowDatabase 组件！", this);
            return;
        }

        string targetScene = 是跳转
            ? 窗口数据库.GetJumpSceneName(窗口ID)
            : 窗口数据库.GetAsyncSceneName(窗口ID);

        if (string.IsNullOrEmpty(targetScene))
        {
            Debug.LogWarning($"[SettingsWindowManager] 数据库中没有 ID 为 {窗口ID} 的{(是跳转 ? "跳转" : "异步")}窗口配置", this);
            return;
        }

        if (是跳转)
        {
            _loading = true;
            StartCoroutine(跳转Routine(targetScene));
            return;
        }

        _当前加载的场景名 = targetScene;
        StartCoroutine(OpenRoutine());
    }

    private IEnumerator 跳转Routine(string 场景名)
    {
        var op = SceneManager.LoadSceneAsync(场景名, LoadSceneMode.Single);
        if (op != null)
            while (!op.isDone) yield return null;

        _loading = false;
    }

    private IEnumerator OpenRoutine()
    {
        _loading = true;

        _windowScene = SceneManager.GetSceneByName(_当前加载的场景名);
        if (!_windowScene.IsValid() || !_windowScene.isLoaded)
        {
            var op = SceneManager.LoadSceneAsync(_当前加载的场景名, LoadSceneMode.Additive);
            if (op != null)
                while (!op.isDone) yield return null;

            _windowScene = SceneManager.GetSceneByName(_当前加载的场景名);
        }

        _window = FindWindow(_windowScene);
        _loading = false;

        if (_window == null)
        {
            Debug.LogWarning($"[SettingsWindowManager] 未在场景 \"{_当前加载的场景名}\" 中找到 SettingsWindow，请确认场景已加入 Build Settings。", this);
            yield break;
        }

        _open = true;
        _window.打开(OnWindowClosed);
    }

    private void OnWindowClosed()
    {
        _open = false;
        if (关闭后卸载场景) StartCoroutine(UnloadRoutine());
    }

    private IEnumerator UnloadRoutine()
    {
        yield return null;

        if (_windowScene.IsValid() && _windowScene.isLoaded)
        {
            var op = SceneManager.UnloadSceneAsync(_windowScene);
            if (op != null)
                while (!op.isDone) yield return null;
        }

        _window = null;
    }

    private static SettingsWindow FindWindow(Scene scene)
    {
        if (!scene.IsValid()) return null;

        foreach (var root in scene.GetRootGameObjects())
        {
            var window = root.GetComponentInChildren<SettingsWindow>(true);
            if (window != null) return window;
        }

        return null;
    }
}