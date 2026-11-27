using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// 轻量提示条：淡入上浮 → 停留 → 淡出，可被重复触发。
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("UI/轻提示 (SimpleToast)")]
public class SimpleToast : MonoBehaviour
{
    [SerializeField] private CanvasGroup 组;
    [SerializeField] private RectTransform 面板;
    [SerializeField] private TMP_Text 文本;

    [SerializeField, Min(0.01f)] private float 淡入时长 = 0.15f;
    [SerializeField, Min(0f)] private float 停留时长 = 1.2f;
    [SerializeField, Min(0.01f)] private float 淡出时长 = 0.25f;
    [SerializeField] private float 上移距离 = 14f;

    private Vector2 _basePos;
    private Coroutine _routine;

    private void Awake()
    {
        if (组 == null) 组 = GetComponent<CanvasGroup>();
        if (组 == null) 组 = gameObject.AddComponent<CanvasGroup>();
        if (面板 == null) 面板 = (RectTransform)transform;

        _basePos = 面板.anchoredPosition;
        组.alpha = 0f;
        组.interactable = false;
        组.blocksRaycasts = false;
    }

    /// <summary>显示提示，可替换文本。</summary>
    public void 显示(string 内容 = null)
    {
        if (文本 != null && !string.IsNullOrEmpty(内容)) 文本.text = 内容;

        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        float t = 0f;
        while (t < 淡入时长)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / 淡入时长);
            组.alpha = p;
            面板.anchoredPosition = _basePos + Vector2.down * (上移距离 * (1f - p));
            yield return null;
        }

        组.alpha = 1f;
        面板.anchoredPosition = _basePos;

        yield return new WaitForSecondsRealtime(停留时长);

        t = 0f;
        while (t < 淡出时长)
        {
            t += Time.unscaledDeltaTime;
            组.alpha = 1f - Mathf.Clamp01(t / 淡出时长);
            yield return null;
        }

        组.alpha = 0f;
        _routine = null;
    }
}
