using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 菜单按钮的入场细节 / 悬停 / 两次点击交互。
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("UI/菜单按钮反馈 (UIMenuButton)")]
public class UIMenuButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
{
    [Header("目标")]
    [Tooltip("整体内容节点（底色 + 竖条 + 选中条 + 文字都在它下面）")]
    [SerializeField] private RectTransform 内容;
    [SerializeField] private Graphic 底色;
    [SerializeField] private RectTransform 左侧条;
    [SerializeField] private TMP_Text 文字;
    [Tooltip("点击后横扫并停留在按钮上的白色高亮")]
    [SerializeField] private RectTransform 选中条;

    [Header("入场（由 UIMenuPopIn 调用）")]
    [SerializeField, Min(0.01f)] private float 条宽_常态 = 3f;
    [SerializeField, Min(0.01f)] private float 条生长时长 = 0.4f;
    [SerializeField, Min(1f)] private float 打字速度 = 34f;

    [Header("悬停")]
    [SerializeField] private float 悬停位移 = 14f;
    [SerializeField] private float 条宽_悬停 = 9f;
    [SerializeField, Range(0f, 1f)] private float 底色透明_常态 = 0.035f;
    [SerializeField, Range(0f, 1f)] private float 底色透明_悬停 = 0.10f;
    [SerializeField] private Color 文字_常态 = new Color(0.80f, 0.83f, 0.88f, 1f);
    [SerializeField] private Color 文字_悬停 = Color.white;

    [Header("选中 / 确认")]
    [SerializeField, Min(0.01f)] private float 选中扫过时长 = 0.2f;
    [SerializeField, Min(0.01f)] private float 取消扫回时长 = 0.16f;
    [SerializeField, Min(0.01f)] private float 确认闪动时长 = 0.22f;
    [SerializeField] private float 确认缩放 = 1.02f;
    [Tooltip("确认后让整个菜单飞出，再触发「点击结束」")]
    [SerializeField] private bool 确认后菜单退场 = false;
    [Tooltip("第二次点击确认后触发（可接场景加载等）")]
    [SerializeField] private UnityEvent 点击结束;

    [Header("窗口设置 (新增)")]
    [Tooltip("填写全局数据库里配置的 ID（比如 setting_audio），确认时会自动加载对应场景")]
    [SerializeField] private string 打开窗口ID;
    [Tooltip("勾选后按「跳转窗口配置列表」直接切换场景；不勾选则按「异步窗口配置列表」叠加打开")]
    [SerializeField] private bool 窗口是否为跳转 = false;

    [Header("过渡")]
    [SerializeField, Min(0.01f)] private float 过渡速度 = 14f;
    [SerializeField] private float 按下缩放 = 0.98f;

    private Vector2 _contentBasePos;
    private Vector3 _contentBaseScale;
    private Graphic _sweepGraphic;
    private Color _sweepBaseColor = Color.white;

    private float _hover;
    private float _barGrow = 1f;
    private float _confirmPulse;
    private float _sweepWidth;

    private bool _isHover;
    private bool _isDown;
    private bool _selected;
    private bool _confirming;

    private Coroutine _introRoutine;
    private Coroutine _sweepRoutine;
    private Coroutine _confirmRoutine;

    public bool 已选中 => _selected;

    private void Awake()
    {
        if (内容 != null)
        {
            _contentBasePos = 内容.anchoredPosition;
            _contentBaseScale = 内容.localScale;
            _sweepWidth = 内容.rect.width - 12f;
        }

        if (选中条 != null)
        {
            _sweepGraphic = 选中条.GetComponent<Graphic>();
            if (_sweepGraphic != null) _sweepBaseColor = _sweepGraphic.color;
        }

        ResetState();
    }

    private void Update()
    {
        _hover = Mathf.MoveTowards(_hover, _isHover && !_selected ? 1f : 0f, Time.unscaledDeltaTime * 过渡速度);
        Apply();

#if ENABLE_INPUT_SYSTEM
        bool mouseDown = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        Vector2 mousePos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
        bool mouseDown = Input.GetMouseButtonDown(0);
        Vector2 mousePos = Input.mousePosition;
#endif

        if (_selected && !_confirming && mouseDown)
        {
            PointerEventData eventData = new PointerEventData(EventSystem.current) { position = mousePos };
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, results);

            bool clickedThis = false;
            foreach (var r in results)
            {
                if (r.gameObject == gameObject || r.gameObject.transform.IsChildOf(transform))
                {
                    clickedThis = true;
                    break;
                }
            }

            if (!clickedThis)
            {
                取消选中();
            }
        }
    }

    private void Apply()
    {
        if (内容 != null)
        {
            float press = _isDown ? 1f : 0f;
            float scale = Mathf.Lerp(1f, 按下缩放, press) * Mathf.Lerp(1f, 确认缩放, _confirmPulse);
            内容.anchoredPosition = _contentBasePos + new Vector2(悬停位移 * _hover - 2f * press, 0f);
            内容.localScale = _contentBaseScale * scale;
        }

        if (左侧条 != null)
        {
            var size = 左侧条.sizeDelta;
            size.x = _barGrow * Mathf.Lerp(条宽_常态, 条宽_悬停, _hover);
            左侧条.sizeDelta = size;

            var barImage = 左侧条.GetComponent<Graphic>();
            if (barImage != null)
                barImage.color = _selected ? new Color(0f, 0f, 0f, 0.85f) : Color.white;
        }

        if (底色 != null)
        {
            var c = 底色.color;
            c.a = Mathf.Lerp(底色透明_常态, 底色透明_悬停, _hover);
            底色.color = c;
        }

        if (文字 != null)
            文字.color = _selected ? Color.black : Color.Lerp(文字_常态, 文字_悬停, _hover);
    }

    public void PlayIntro(float delay)
    {
        _barGrow = 0f;
        if (文字 != null) 文字.maxVisibleCharacters = 0;

        if (_introRoutine != null) StopCoroutine(_introRoutine);
        _introRoutine = StartCoroutine(RunIntro(delay));
    }

    public void StopIntro()
    {
        if (_introRoutine != null) StopCoroutine(_introRoutine);
        _barGrow = 1f;
        if (文字 != null) 文字.maxVisibleCharacters = 99999;
    }

    public void ResetState()
    {
        _selected = false;
        _confirming = false;
        _confirmPulse = 0f;
        _barGrow = 1f;

        if (_introRoutine != null) StopCoroutine(_introRoutine);
        if (_sweepRoutine != null) StopCoroutine(_sweepRoutine);
        if (_confirmRoutine != null) StopCoroutine(_confirmRoutine);
        _introRoutine = _sweepRoutine = _confirmRoutine = null;

        SetSweepWidth(0f);
        if (_sweepGraphic != null) _sweepGraphic.color = _sweepBaseColor;
        if (文字 != null) 文字.maxVisibleCharacters = 99999;
    }

    private IEnumerator RunIntro(float delay)
    {
        yield return WaitUnscaled(delay);
        if (文字 == null) yield break;

        文字.ForceMeshUpdate();
        int total = 文字.textInfo.characterCount;
        文字.maxVisibleCharacters = 0;
        _barGrow = 0f;

        float barT = 0f;
        float typed = 0f;
        while (barT < 1f || typed < total)
        {
            float dt = Time.unscaledDeltaTime;

            if (barT < 1f)
            {
                barT = Mathf.Clamp01(barT + dt / 条生长时长);
                _barGrow = 1f - Mathf.Pow(1f - barT, 3);
            }

            if (typed < total)
            {
                typed = Mathf.Min(total, typed + dt * 打字速度);
                文字.maxVisibleCharacters = Mathf.FloorToInt(typed);
            }

            yield return null;
        }

        文字.maxVisibleCharacters = total;
        _barGrow = 1f;
        _introRoutine = null;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (_confirming) return;

        if (_selected)
        {
            确认();
            return;
        }

        var group = GetComponentInParent<UIMenuPopIn>();
        if (group != null) group.切换选中(this);
        else 进入选中();
    }

    public void 进入选中()
    {
        if (_selected) return;
        _selected = true;
        if (_sweepRoutine != null) StopCoroutine(_sweepRoutine);
        _sweepRoutine = StartCoroutine(SweepTo(1f, 选中扫过时长));
    }

    public void 取消选中()
    {
        if (!_selected) return;
        _selected = false;
        if (_sweepRoutine != null) StopCoroutine(_sweepRoutine);
        _sweepRoutine = StartCoroutine(SweepTo(0f, 取消扫回时长));
    }

    public void 确认()
    {
        if (!_selected || _confirming) return;
        _confirmRoutine = StartCoroutine(RunConfirm());
    }

    private IEnumerator RunConfirm()
    {
        _confirming = true;

        float t = 0f;
        while (t < 确认闪动时长)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / 确认闪动时长);
            _confirmPulse = Mathf.Sin(p * Mathf.PI);

            if (_sweepGraphic != null)
            {
                var c = _sweepBaseColor;
                c.a = Mathf.Lerp(_sweepBaseColor.a, 1f, _confirmPulse);
                _sweepGraphic.color = c;
            }

            yield return null;
        }

        _confirmPulse = 0f;
        if (_sweepGraphic != null) _sweepGraphic.color = _sweepBaseColor;

        if (确认后菜单退场)
        {
            var group = GetComponentInParent<UIMenuPopIn>();
            float wait = group != null ? group.退场() : 0f;
            if (wait > 0f) yield return WaitUnscaled(wait);
        }

        // 如果面板上填了 ID，自动寻找 Manager 并打开
        if (!string.IsNullOrEmpty(打开窗口ID))
        {
#if UNITY_2023_1_OR_NEWER
            // 【已修复】将 FindFirstObjectByType 替换为 FindAnyObjectByType 解决警告
            var manager = FindAnyObjectByType<SettingsWindowManager>();
#else
            var manager = FindObjectOfType<SettingsWindowManager>();
#endif
            if (manager != null)
            {
                manager.打开设置(打开窗口ID, 窗口是否为跳转);
            }
            else
            {
                Debug.LogWarning($"[UIMenuButton] 找不到 SettingsWindowManager，无法打开窗口ID：{打开窗口ID}", this);
            }
        }

        点击结束?.Invoke();
        _confirming = false;
        _confirmRoutine = null;
    }

    private IEnumerator SweepTo(float target, float duration)
    {
        if (选中条 == null) yield break;

        float from = _sweepWidth > 0f ? 选中条.sizeDelta.x / _sweepWidth : 0f;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / duration);
            float e = 1f - (1f - p) * (1f - p);
            SetSweepWidth(Mathf.Lerp(from, target, e) * _sweepWidth);
            yield return null;
        }

        SetSweepWidth(target * _sweepWidth);
        _sweepRoutine = null;
    }

    private void SetSweepWidth(float width)
    {
        if (选中条 == null) return;
        var size = 选中条.sizeDelta;
        size.x = width;
        选中条.sizeDelta = size;
    }

    public void OnPointerEnter(PointerEventData eventData) => _isHover = true;

    public void OnPointerExit(PointerEventData eventData)
    {
        _isHover = false;
        _isDown = false;
    }

    public void OnPointerDown(PointerEventData eventData) => _isDown = true;

    public void OnPointerUp(PointerEventData eventData) => _isDown = false;

    private static IEnumerator WaitUnscaled(float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }
    }
}