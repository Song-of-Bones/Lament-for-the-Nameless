using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 场景流程管理器：场景本身只负责把自己的过场动画播完，跳到哪个场景由本场景
/// WindowDatabase 的「跳转窗口配置」列表（流程 ID → 场景名）决定。
/// 脚本里不再写死目标场景，这条配置可以在场景连接图里直接拖线维护。
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("场景/场景流程管理 (SceneFlowManager)")]
public class SceneFlowManager : MonoBehaviour
{
    [Header("配置")]
    [Tooltip("留空会自动在本场景里查找 WindowDatabase")]
    [SerializeField] private WindowDatabase 窗口数据库;

    [Tooltip("目标场景用异步加载，避免切场景时卡顿")]
    [SerializeField] private bool 异步加载 = true;

    private bool _running;
    private string _preloadScene;
    private AsyncOperation _preload;

    /// <summary>是否已发起过跳转。</summary>
    public bool 执行中 => _running;

    private void Awake()
    {
        if (窗口数据库 == null) 窗口数据库 = FindAnyObjectByType<WindowDatabase>();
    }

    /// <summary>按流程 ID 在跳转列表里预加载目标场景（只加载不激活，切换时才显示）。</summary>
    public void Preload(string 流程ID)
    {
        if (!异步加载 || string.IsNullOrEmpty(流程ID)) return;
        if (_preload != null && _preloadScene == 流程ID) return;

        string 目标场景 = Resolve(流程ID);
        if (string.IsNullOrEmpty(目标场景) || !Application.CanStreamedLevelBeLoaded(目标场景)) return;

        _preload = SceneManager.LoadSceneAsync(目标场景);
        if (_preload == null) return;

        _preloadScene = 目标场景;
        _preload.allowSceneActivation = false;
    }

    /// <summary>过场播完后调用：把「流程 ID」交给管理器，由数据库决定跳到哪。</summary>
    public void Play(string 流程ID)
    {
        if (_running) return;

        string 目标场景 = Resolve(流程ID);
        if (string.IsNullOrEmpty(目标场景))
        {
            Debug.LogWarning(
                $"[SceneFlowManager] WindowDatabase 的跳转列表里没有流程 ID「{流程ID}」，无法跳转。", this);
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(目标场景))
        {
            Debug.LogWarning($"[SceneFlowManager] 场景「{目标场景}」无法加载，请确认已加入 Build Settings。", this);
            return;
        }

        _running = true;
        StartCoroutine(Run(目标场景));
    }

    /// <summary>解析流程 ID → 目标场景名（找不到返回 null）。</summary>
    public string Resolve(string 流程ID)
    {
        if (窗口数据库 == null) 窗口数据库 = FindAnyObjectByType<WindowDatabase>();
        return 窗口数据库 != null ? 窗口数据库.GetJumpSceneName(流程ID) : null;
    }

    /// <summary>在本场景里查找管理器（没有则返回 null）。</summary>
    public static SceneFlowManager Find()
    {
        return FindAnyObjectByType<SceneFlowManager>(FindObjectsInactive.Include);
    }

    private IEnumerator Run(string 目标场景)
    {
        if (_preload != null && _preloadScene == 目标场景)
        {
            _preload.allowSceneActivation = true;
            yield break;
        }

        if (异步加载)
        {
            var op = SceneManager.LoadSceneAsync(目标场景);
            if (op != null) while (!op.isDone) yield return null;
        }
        else
        {
            SceneManager.LoadScene(目标场景);
        }

        _running = false;
    }
}
