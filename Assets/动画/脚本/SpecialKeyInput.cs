using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 专项密钥输入：6 个卡片位，隐藏的 TMP_InputField 负责接收输入。
/// 只有输入域获得焦点时才高亮当前待输入卡片；每输入一位会播放一次卡片弹跳 + 字符淡入。
/// 输入只允许数字与大写字母，回车触发「提交」事件。
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("UI/专项密钥输入 (SpecialKeyInput)")]
public class SpecialKeyInput : MonoBehaviour
{
    [Header("输入")]
    [SerializeField] private TMP_InputField 输入域;
    [SerializeField] private RectTransform[] 卡片;
    [SerializeField] private TMP_Text[] 字位;
    [SerializeField] private Graphic[] 边框;

    [Header("配色")]
    [SerializeField] private Color 边框_空 = new Color(1f, 1f, 1f, 0.14f);
    [SerializeField] private Color 边框_已填 = new Color(1f, 1f, 1f, 0.50f);
    [SerializeField] private Color 边框_当前 = new Color(1f, 1f, 1f, 0.85f);
    [SerializeField] private Color 字符颜色 = new Color(0.95f, 0.96f, 1f, 1f);

    [Header("动画")]
    [SerializeField, Min(0.01f)] private float 弹跳时长 = 0.18f;
    [SerializeField, Min(0f)] private float 弹跳幅度 = 0.12f;

    [Header("提交")]
    [SerializeField] private SimpleToast 提示;
    [SerializeField] private string 提交提示 = "专项密钥已提交";
    [SerializeField] private UnityEvent<string> 提交;

    private bool _focused;
    private int _lastLength;

    public string 当前密钥 => 输入域 != null ? 输入域.text : string.Empty;

    private void Awake()
    {
        if (输入域 != null)
        {
            输入域.characterLimit = 6;
            输入域.onValueChanged.AddListener(OnValueChanged);
            输入域.onSubmit.AddListener(OnSubmit);
            输入域.onSelect.AddListener(_ => OnSelect());
            输入域.onDeselect.AddListener(_ => OnDeselect());
        }

        刷新();
        _lastLength = 当前密钥.Length;
    }

    private void OnSelect()
    {
        _focused = true;
        刷新();
    }

    private void OnDeselect()
    {
        _focused = false;
        刷新();
    }

    private void Update()
    {
        if (!_focused || 输入域 == null || 边框 == null) return;

        string key = 当前密钥;
        if (key.Length >= 边框.Length) return;

        var border = 边框[key.Length];
        if (border == null) return;

        var c = 边框_当前;
        c.a = 0.55f + 0.30f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f));
        border.color = c;
    }

    private void OnValueChanged(string value)
    {
        string clean = 过滤(value);

        if (输入域 != null && clean != value)
            输入域.SetTextWithoutNotify(clean);

        int previous = _lastLength;
        刷新();
        _lastLength = clean.Length;

        for (int i = previous; i < clean.Length; i++)
            StartCoroutine(插入动画(i));
    }

    public void 刷新()
    {
        string key = 当前密钥;

        if (字位 != null)
        {
            for (int i = 0; i < 字位.Length; i++)
            {
                if (字位[i] == null) continue;
                字位[i].text = i < key.Length ? key[i].ToString() : string.Empty;
                字位[i].color = 字符颜色;
            }
        }

        if (边框 != null)
        {
            for (int i = 0; i < 边框.Length; i++)
            {
                if (边框[i] == null) continue;

                bool filled = i < key.Length;
                bool current = _focused && i == key.Length;
                边框[i].color = filled ? 边框_已填 : (current ? 边框_当前 : 边框_空);
            }
        }
    }

    private IEnumerator 插入动画(int index)
    {
        if (卡片 == null || index < 0 || index >= 卡片.Length) yield break;

        var rt = 卡片[index];
        var text = 字位 != null && index < 字位.Length ? 字位[index] : null;
        var border = 边框 != null && index < 边框.Length ? 边框[index] : null;

        Vector3 baseScale = rt != null ? rt.localScale : Vector3.one;
        float t = 0f;
        while (t < 弹跳时长)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / 弹跳时长);
            float pop = 1f + 弹跳幅度 * Mathf.Sin(p * Mathf.PI);

            if (rt != null) rt.localScale = baseScale * pop;
            if (text != null)
            {
                var c = 字符颜色;
                c.a = Mathf.Clamp01(p * 1.6f);
                text.color = c;
            }
            if (border != null) border.color = Color.Lerp(边框_当前, 边框_已填, p);

            yield return null;
        }

        if (rt != null) rt.localScale = baseScale;
        if (text != null) text.color = 字符颜色;
        if (border != null) border.color = 边框_已填;
    }

    private void OnSubmit(string value)
    {
        if (提示 != null) 提示.显示(提交提示);
        提交?.Invoke(value);
    }

    public void 聚焦()
    {
        if (输入域 != null) 输入域.ActivateInputField();
    }

    private static string 过滤(string raw)
    {
        var sb = new StringBuilder(6);
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        foreach (char c in raw.ToUpperInvariant())
        {
            if (sb.Length >= 6) break;
            if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z')) sb.Append(c);
        }

        return sb.ToString();
    }
}
