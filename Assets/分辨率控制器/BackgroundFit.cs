using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 背景图自适应：以 2400×1080 为参考画面做 cover（等比放大铺满，多余部分从中心裁掉）。
/// 例如屏幕变成 2500×1080 时，背景放大到 2500×1125，只裁掉上下各 22.5 像素。
/// 关闭「分辨率自适应」时恢复为直接拉伸铺满。
/// </summary>
[RequireComponent(typeof(Image))]
[DisallowMultipleComponent]
[AddComponentMenu("UI/背景自适应 (BackgroundFit)")]
public class BackgroundFit : MonoBehaviour
{
    [Header("参考画面")]
    [Tooltip("设计时的横屏尺寸，决定裁切基准")]
    [SerializeField] private Vector2 参考分辨率 = new Vector2(2400f, 1080f);

    private Image _image;
    private RectTransform _rect;
    private RectTransform _parent;
    private Vector2 _lastParentSize;

    private void Awake()
    {
        _image = GetComponent<Image>();
        _rect = (RectTransform)transform;
        _parent = _rect.parent as RectTransform;

        ResolutionAdapt.改变 += OnSettingChanged;
        应用();
    }

    private void OnDestroy()
    {
        ResolutionAdapt.改变 -= OnSettingChanged;
    }

    private void Update()
    {
        if (_parent == null) return;

        Vector2 size = _parent.rect.size;
        if (size != _lastParentSize) 应用();
    }

    private void OnSettingChanged(bool _) => 应用();

    /// <summary>重新计算背景尺寸。</summary>
    public void 应用()
    {
        if (_image == null || _parent == null) return;

        Vector2 screen = _parent.rect.size;
        _lastParentSize = screen;
        if (screen.x <= 0f || screen.y <= 0f) return;

        _rect.anchorMin = Vector2.zero;
        _rect.anchorMax = Vector2.one;
        _rect.pivot = new Vector2(0.5f, 0.5f);
        _rect.anchoredPosition = Vector2.zero;

        if (!ResolutionAdapt.已开启)
        {
            // 关闭：直接拉伸铺满
            _rect.sizeDelta = Vector2.zero;
            return;
        }

        Vector2 reference = 参考分辨率;
        if (reference.x <= 0f || reference.y <= 0f)
        {
            reference = new Vector2(2400f, 1080f);
        }

        // cover：按较“紧”的一边等比放大，另一方向多出的部分被裁掉
        float scale = Mathf.Max(screen.x / reference.x, screen.y / reference.y);
        Vector2 display = reference * scale;

        // 锚点拉伸时不改 rect，只补上超出屏幕的尺寸；超出部分由屏幕边界自然裁掉
        _rect.sizeDelta = new Vector2(
            Mathf.Max(0f, display.x - screen.x),
            Mathf.Max(0f, display.y - screen.y));
    }
}
