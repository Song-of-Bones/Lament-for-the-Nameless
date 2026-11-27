using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 设置窗口：淡入遮罩 + 面板回弹放大，关闭时反向收回。
/// 由 SettingsWindowManager 在异步加载后调用 打开()，关闭动画结束后回调。
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("UI/设置窗口 (SettingsWindow)")]
public class SettingsWindow : MonoBehaviour
{
    [Header("结构")]
    [SerializeField] private RectTransform 面板;
    [SerializeField] private CanvasGroup 内容组;
    [SerializeField] private Graphic 遮罩;
    [SerializeField] private Button 关闭按钮;

    [Header("参数")]
    [SerializeField, Range(0f, 1f)] private float 遮罩不透明度 = 0.55f;
    [SerializeField, Min(0.01f)] private float 打开时长 = 0.38f;
    [SerializeField, Min(0.01f)] private float 关闭时长 = 0.22f;
    [SerializeField, Range(0.5f, 1f)] private float 起始缩放 = 0.9f;
    [SerializeField] private float 起始上移 = 24f;
    [SerializeField] private bool 允许ESC关闭 = true;

    private Vector2 _panelBasePos;
    private Action _onClosed;
    private Coroutine _routine;
    private bool _closing;

    private void Awake()
    {
        if (面板 != null) _panelBasePos = 面板.anchoredPosition;

        if (遮罩 != null)
        {
            var c = 遮罩.color;
            c.a = 0f;
            遮罩.color = c;
        }

        if (内容组 != null) 内容组.alpha = 0f;
        if (关闭按钮 != null) 关闭按钮.onClick.AddListener(关闭);

        Apply(0f);
    }

    private void Update()
    {
        if (!允许ESC关闭 || _closing) return;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) 关闭();
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.Escape)) 关闭();
#endif
    }

    /// <summary>播放打开动画；关闭动画结束后回调 onClosed。</summary>
    public void 打开(Action onClosed = null)
    {
        _onClosed = onClosed;
        _closing = false;

        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(RunOpen());
    }

    /// <summary>播放关闭动画，结束后回调并交给管理器卸载场景。</summary>
    public void 关闭()
    {
        if (_closing) return;
        _closing = true;

        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(RunClose());
    }

    private IEnumerator RunOpen()
    {
        float t = 0f;
        while (t < 打开时长)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / 打开时长);
            Apply(p);
            yield return null;
        }

        Apply(1f);
        _routine = null;
    }

    private IEnumerator RunClose()
    {
        float t = 0f;
        float from = 内容组 != null ? 内容组.alpha : 1f;
        float maskFrom = 遮罩 != null ? 遮罩.color.a : 0f;
        Vector2 posFrom = 面板 != null ? 面板.anchoredPosition : _panelBasePos;
        Vector3 scaleFrom = 面板 != null ? 面板.localScale : Vector3.one;

        while (t < 关闭时长)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / 关闭时长);
            float e = p * p; // easeInQuad，收起更快

            if (内容组 != null) 内容组.alpha = Mathf.Lerp(from, 0f, e);
            if (遮罩 != null)
            {
                var c = 遮罩.color;
                c.a = Mathf.Lerp(maskFrom, 0f, e);
                遮罩.color = c;
            }
            if (面板 != null)
            {
                面板.anchoredPosition = Vector2.Lerp(posFrom, _panelBasePos + Vector2.down * 起始上移, e);
                float s = Mathf.Lerp(scaleFrom.x, 起始缩放 * 0.98f, e);
                面板.localScale = new Vector3(s, s, 1f);
            }

            yield return null;
        }

        if (内容组 != null) 内容组.alpha = 0f;
        if (遮罩 != null)
        {
            var c = 遮罩.color;
            c.a = 0f;
            遮罩.color = c;
        }

        _routine = null;
        _onClosed?.Invoke();
        _onClosed = null;
    }

    private void Apply(float p)
    {
        float maskE = Mathf.Clamp01(p / 0.55f);
        float panelP = Mathf.Clamp01((p - 0.08f) / 0.92f);
        float back = EaseOutBack(panelP, 1.15f);

        if (遮罩 != null)
        {
            var c = 遮罩.color;
            c.a = Mathf.Lerp(0f, 遮罩不透明度, 1f - Mathf.Pow(1f - maskE, 2f));
            遮罩.color = c;
        }

        if (面板 != null)
        {
            float s = Mathf.LerpUnclamped(起始缩放, 1f, back);
            面板.localScale = new Vector3(s, s, 1f);
            面板.anchoredPosition = _panelBasePos + Vector2.down * (起始上移 * (1f - back));
        }

        if (内容组 != null)
            内容组.alpha = Mathf.Clamp01(panelP * 1.4f);
    }

    private static float EaseOutBack(float p, float overshoot)
    {
        float c1 = 1.70158f * Mathf.Max(0f, overshoot);
        float c3 = c1 + 1f;
        float x = p - 1f;
        return 1f + c3 * x * x * x + c1 * x * x;
    }
}
