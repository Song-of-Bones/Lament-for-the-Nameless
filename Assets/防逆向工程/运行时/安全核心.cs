using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 安全核心：自动创建、跨场景常驻，负责汇总各项校验并按配置响应。
/// 资源清单在启动时立即载入（不等首轮延迟），避免第一个场景就加载加密资源时清单尚未就绪。
/// 其他系统可通过 安全核心.受信任 判断当前环境，或订阅 检测到异常 事件做自定义处理。
/// </summary>
[DefaultExecutionOrder(-10000)]
[DisallowMultipleComponent]
public class 安全核心 : MonoBehaviour
{
    public static 安全核心 实例 { get; private set; }

    /// <summary>所有检测均通过时为 true；一旦发现异常会置为 false。</summary>
    public static bool 受信任 { get; private set; } = true;

    /// <summary>发现异常时触发，参数为可读原因。适合用来弹提示或上报。</summary>
    public static event Action<string> 检测到异常;

    private bool _已报告;
    private float _下次调试器检测;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void 自动创建()
    {
        if (实例 != null) return;

        var 物体 = new GameObject("[防逆向]");
        物体.hideFlags = HideFlags.HideAndDontSave;
        DontDestroyOnLoad(物体);
        物体.AddComponent<安全核心>();
    }

    /// <summary>进入运行前重置静态状态，避免关闭域重载时沿用上一次会话的结果。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void 重置静态状态()
    {
        实例 = null;
        受信任 = true;
        检测到异常 = null;
    }

    private void Awake()
    {
        if (实例 != null)
        {
            Destroy(gameObject);
            return;
        }

        实例 = this;
        DontDestroyOnLoad(gameObject);

        加密资源.清理资源包缓存();
        StartCoroutine(完整性校验.载入资源清单((正常, 说明) =>
        {
            // 清单缺失不在这里判死：只有真正加载加密资源时才会因“清单未就绪”被拒绝，
            // 避免项目还没启用加密资源时就报错。
            if (!正常) 安全日志.警告(说明);
        }));
    }

    private void Start()
    {
        StartCoroutine(首轮检测());
        if (安全配置.启用反调试) 反调试检测.启动();
    }

    private IEnumerator 首轮检测()
    {
        yield return new WaitForSecondsRealtime(安全配置.首次检测延迟);

        string 原因;
        if (!完整性校验.校验正版(out 原因)) 报告(原因);

        string 实测;
        if (!完整性校验.校验安卓签名(out 实测))
            报告("APK 签名与配置不一致（实测：" + 实测 + "）。");
    }

    private void Update()
    {
        if (_已报告) return;

        if (安全配置.启用反调试)
        {
            if (反调试检测.发现异常)
            {
                报告(反调试检测.详情);
                return;
            }

            if (Time.unscaledTime >= _下次调试器检测)
            {
                _下次调试器检测 = Time.unscaledTime + 安全配置.检测间隔;
                if (System.Diagnostics.Debugger.IsAttached) 报告("检测到调试器连接。");
            }
        }
    }

    private void 报告(string 原因)
    {
        if (_已报告) return;
        if (string.IsNullOrEmpty(原因)) 原因 = "安全检查未通过。";

        _已报告 = true;
        受信任 = false;
        安全日志.错误(原因);

        var 事件 = 检测到异常;
        if (事件 != null)
        {
            try
            {
                事件(原因);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        switch (安全配置.响应方式)
        {
            case 1:
                加密资源.禁用();
                break;

            case 2:
                StartCoroutine(退出());
                break;
        }
    }

    private IEnumerator 退出()
    {
        yield return new WaitForSecondsRealtime(0.2f);

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
