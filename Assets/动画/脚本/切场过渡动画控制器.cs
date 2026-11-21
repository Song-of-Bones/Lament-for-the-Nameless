using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 大厅入场衔接：黑场停留 → 黑场渐隐 + 背景缓慢拉远 → 菜单随后弹入。
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("场景/大厅入场衔接 (LobbyIntro)")]
public class LobbyIntro : MonoBehaviour
{
    [Header("黑场遮罩")]
    [SerializeField] private Image 遮罩;
    [Tooltip("完全黑场的停留时间，给上一场景的切黑留出余韵")]
    [SerializeField, Min(0f)] private float 黑场停留 = 0.15f;
    [SerializeField, Min(0.01f)] private float 淡出时长 = 1.1f;

    [Header("背景")]
    [SerializeField] private RectTransform 背景;
    [Tooltip("开场时背景略微放大，制造“镜头拉远”的到场感")]
    [SerializeField, Min(1f)] private float 起始缩放 = 1.06f;
    [SerializeField, Min(1f)] private float 稳定缩放 = 1.01f;
    [SerializeField, Min(0.01f)] private float 缩放时长 = 1.9f;

    [Header("菜单")]
    [SerializeField] private UIMenuPopIn 菜单;
    [Tooltip("进入场景后多久开始弹按钮，和背景渐显错开")]
    [SerializeField, Min(0f)] private float 菜单延迟 = 0.5f;

    [Header("雪花过渡")]
    [Tooltip("承接上一场景结尾的静电噪点，短暂残留后消失")]
    [SerializeField] private Graphic 雪花;
    [SerializeField] private Material 雪花材质;
    [SerializeField, Range(0f, 1f)] private float 雪花起始透明 = 0.14f;
    [SerializeField, Min(0.01f)] private float 雪花时长 = 0.6f;

    [Header("尾注")]
    [SerializeField] private CanvasGroup 尾注;
    [SerializeField, Min(0f)] private float 尾注延迟 = 1.2f;
    [SerializeField, Min(0.01f)] private float 尾注时长 = 0.7f;

    private Material _snowMaterial;
    private CanvasGroup _snowGroup;

    private void Awake()
    {
        if (背景 != null) 背景.localScale = Vector3.one * 起始缩放;
        if (遮罩 != null) 遮罩.color = new Color(0f, 0f, 0f, 1f);
        if (尾注 != null) 尾注.alpha = 0f;

        if (雪花 != null)
        {
            _snowGroup = 雪花.GetComponent<CanvasGroup>();
            if (_snowGroup == null) _snowGroup = 雪花.gameObject.AddComponent<CanvasGroup>();
            _snowGroup.alpha = 雪花起始透明;

            var source = 雪花材质 != null ? 雪花材质 : 雪花.material;
            if (source != null)
            {
                _snowMaterial = new Material(source);
                雪花.material = _snowMaterial;
            }
        }
    }

    private void Start()
    {
        // 关键：进场第一帧就把按钮收进入场姿态，避免它们在黑场下已经停在最终位置
        if (菜单 != null) 菜单.隐藏();

        StartCoroutine(Run());
    }

    private void Update()
    {
        if (_snowMaterial != null)
            _snowMaterial.SetFloat("_Phase", Time.unscaledTime * 1.2f);
    }

    private IEnumerator Run()
    {
        StartCoroutine(FadeOverlay());
        StartCoroutine(SettleBackground());
        StartCoroutine(FadeSnow());
        if (尾注 != null) StartCoroutine(FadeNotice());

        yield return Wait(菜单延迟);
        if (菜单 != null) 菜单.Play();
    }

    private IEnumerator FadeOverlay()
    {
        if (遮罩 == null) yield break;

        yield return Wait(黑场停留);

        float t = 0f;
        while (t < 淡出时长)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / 淡出时长);
            float e = Mathf.SmoothStep(0f, 1f, p); // 缓入缓出，黑场多留一会儿
            var c = 遮罩.color;
            c.a = 1f - e;
            遮罩.color = c;
            yield return null;
        }

        var final = 遮罩.color;
        final.a = 0f;
        遮罩.color = final;
        遮罩.gameObject.SetActive(false);
    }

    private IEnumerator SettleBackground()
    {
        if (背景 == null) yield break;

        float t = 0f;
        while (t < 缩放时长)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / 缩放时长);
            float e = 1f - Mathf.Pow(1f - p, 3);

            float s = Mathf.Lerp(起始缩放, 稳定缩放, e);
            背景.localScale = new Vector3(s, s, 1f);
            yield return null;
        }

        背景.localScale = new Vector3(稳定缩放, 稳定缩放, 1f);
    }

    private IEnumerator FadeSnow()
    {
        if (_snowGroup == null) yield break;

        float t = 0f;
        while (t < 雪花时长)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / 雪花时长);
            _snowGroup.alpha = Mathf.Lerp(雪花起始透明, 0f, 1f - Mathf.Pow(1f - p, 2));
            yield return null;
        }

        _snowGroup.alpha = 0f;
        雪花.gameObject.SetActive(false);
    }

    private IEnumerator FadeNotice()
    {
        yield return Wait(尾注延迟);

        float t = 0f;
        while (t < 尾注时长)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / 尾注时长);
            尾注.alpha = 1f - Mathf.Pow(1f - p, 3);
            yield return null;
        }

        尾注.alpha = 1f;
    }

    private static IEnumerator Wait(float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }
    }
}