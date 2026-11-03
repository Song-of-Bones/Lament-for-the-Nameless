using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 菜单依次弹入：只对 anchoredPosition 叠加位移增量，布局位置仍由锚点决定，
/// 因此换分辨率 / 宽高比时按钮始终贴着画面左缘，动画不用重做。
/// 同时驱动每个按钮上的 UIMenuButton 做条生长 + 文字逐字浮现。
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("UI/菜单弹入 (UIMenuPopIn)")]
public class UIMenuPopIn : MonoBehaviour
{
    [Header("目标")]
    [Tooltip("要依次弹入的按钮；留空则自动收集直接子物体")]
    [SerializeField] private RectTransform[] 目标;

    [Header("入场节奏")]
    [SerializeField, Min(0f)] private float 起始延迟 = 0.10f;
    [SerializeField, Min(0f)] private float 每项延迟 = 0.085f;
    [SerializeField, Min(0.01f)] private float 动画时长 = 0.5f;

    [Header("入场姿态")]
    [Tooltip("从左侧弹入的距离（画布单位，随 Canvas 缩放自动适配）")]
    [SerializeField] private float 滑入距离 = 320f;
    [Tooltip("起始旋转角度，负数表示向左下摆入")]
    [SerializeField] private float 起始角度 = -7f;
    [SerializeField, Range(0.1f, 1f)] private float 起始缩放 = 0.88f;
    [Tooltip("回弹强度，0 = 不回弹")]
    [SerializeField, Range(0f, 2f)] private float 回弹 = 1.15f;
    [Tooltip("淡入在整段动画中的时间占比")]
    [SerializeField, Range(0.05f, 1f)] private float 淡入占比 = 0.35f;

    [Header("退场（点击选中后整体飞出）")]
    [SerializeField, Min(0f)] private float 退场间隔 = 0.045f;
    [SerializeField, Min(0.01f)] private float 退场时长 = 0.38f;

    [Header("其它")]
    [SerializeField] private bool 不受时间缩放影响 = true;

    private class Item
    {
        public RectTransform rect;
        public CanvasGroup group;
        public UIMenuButton motion;
        public Vector2 basePos;
        public Vector3 baseScale;
        public Quaternion baseRot;
    }

    private readonly List<Item> _items = new List<Item>();
    private Coroutine _routine;

    private void Awake()
    {
        Collect();
    }

    private void OnEnable()
    {
        if (_items.Count == 0) Collect();
        Play();
    }

    /// <summary>重新播放入场动画。</summary>
    public void Play()
    {
        if (_items.Count == 0) Collect();
        if (_routine != null) StopAllCoroutines();

        for (int i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            it.group.alpha = 0f;
            it.rect.anchoredPosition = it.basePos + Vector2.left * 滑入距离;
            it.rect.localScale = it.baseScale * 起始缩放;
            it.rect.localRotation = Quaternion.Euler(0f, 0f, 起始角度);

            it.motion?.ResetState();
            it.motion?.PlayIntro(起始延迟 + i * 每项延迟);
        }

        _routine = StartCoroutine(RunAll());
    }

    /// <summary>切换选中项：目标按钮进入选中，其余按钮取消选中。</summary>
    public void 切换选中(UIMenuButton target)
    {
        if (_items.Count == 0) Collect();

        for (int i = 0; i < _items.Count; i++)
        {
            var motion = _items[i].motion;
            if (motion == null) continue;

            if (motion == target) motion.进入选中();
            else motion.取消选中();
        }
    }

    /// <summary>整个菜单依次飞出，返回动画总时长（秒）。</summary>
    public float 退场()
    {
        if (_items.Count == 0) Collect();
        if (_routine != null) StopAllCoroutines();

        for (int i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            it.motion?.StopIntro();
            StartCoroutine(ExitItem(it, (_items.Count - 1 - i) * 退场间隔, 退场时长));
        }

        return (_items.Count - 1) * 退场间隔 + 退场时长;
    }

    private void Collect()
    {
        _items.Clear();

        var list = new List<RectTransform>();
        if (目标 != null && 目标.Length > 0)
        {
            list.AddRange(目标);
        }
        else
        {
            foreach (Transform child in transform)
                if (child is RectTransform rt) list.Add(rt);
        }

        foreach (var rt in list)
        {
            if (rt == null) continue;

            var group = rt.GetComponent<CanvasGroup>();
            if (group == null) group = rt.gameObject.AddComponent<CanvasGroup>();

            _items.Add(new Item
            {
                rect = rt,
                group = group,
                motion = rt.GetComponent<UIMenuButton>(),
                basePos = rt.anchoredPosition,
                baseScale = rt.localScale,
                baseRot = rt.localRotation,
            });
        }
    }

    private IEnumerator RunAll()
    {
        for (int i = 0; i < _items.Count; i++)
            StartCoroutine( Animate(_items[i], 起始延迟 + i * 每项延迟) );

        yield return null;
        _routine = null;
    }

    private IEnumerator Animate(Item it, float delay)
    {
        yield return Wait(delay);
        if (it.rect == null) yield break;

        float elapsed = 0f;
        while (elapsed < 动画时长)
        {
            elapsed += Delta;
            float p = Mathf.Clamp01(elapsed / 动画时长);
            float e = EaseOutBack(p, 回弹);

            it.rect.anchoredPosition = it.basePos + Vector2.left * (滑入距离 * (1f - e));
            it.rect.localScale = Vector3.LerpUnclamped(it.baseScale * 起始缩放, it.baseScale, e);
            it.rect.localRotation = Quaternion.Euler(0f, 0f, 起始角度 * (1f - e));
            it.group.alpha = Mathf.Clamp01(p / 淡入占比);
            yield return null;
        }

        if (it.rect == null) yield break;
        it.rect.anchoredPosition = it.basePos;
        it.rect.localScale = it.baseScale;
        it.rect.localRotation = it.baseRot;
        it.group.alpha = 1f;
    }

    private IEnumerator ExitItem(Item it, float delay, float duration)
    {
        yield return Wait(delay);
        if (it.rect == null) yield break;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Delta;
            float p = Mathf.Clamp01(elapsed / duration);
            float e = p * p; // easeInQuad：越飞越快

            it.rect.anchoredPosition = it.basePos + Vector2.left * (滑入距离 * 1.15f * e);
            it.rect.localRotation = Quaternion.Euler(0f, 0f, 6f * e);
            it.rect.localScale = it.baseScale * Mathf.Lerp(1f, 0.96f, e);
            it.group.alpha = 1f - e;
            yield return null;
        }

        if (it.group != null) it.group.alpha = 0f;
    }

    private float Delta => 不受时间缩放影响 ? Time.unscaledDeltaTime : Time.deltaTime;

    private IEnumerator Wait(float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Delta;
            yield return null;
        }
    }

    private static float EaseOutBack(float p, float overshoot)
    {
        float c1 = 1.70158f * Mathf.Max(0f, overshoot);
        float c3 = c1 + 1f;
        float x = p - 1f;
        return 1f + c3 * x * x * x + c1 * x * x;
    }
}
