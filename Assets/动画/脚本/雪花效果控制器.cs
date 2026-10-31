using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 标题故障驱动：由 Animator 动画 m_GlitchAmount，把强度换算成着色器参数。
/// 幅度随强度线性增强，刷新频率随强度平方增长，形成“越来越快直到爆掉”的收尾。
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("文本/标题故障驱动 (GlitchRamp)")]
public class GlitchRamp : MonoBehaviour
{
    [Header("动画驱动")]
    [Tooltip("0 = 无故障，1 = 峰值（白闪前）")]
    [SerializeField, Range(0f, 1f)] private float m_GlitchAmount;

    [Header("目标")]
    [SerializeField] private Graphic 标题故障;
    [SerializeField] private Graphic 雪花;
    [SerializeField] private Material 标题故障材质;
    [SerializeField] private Material 雪花材质;

    private Material _rgb;
    private Material _snow;
    private float _rgbStep;
    private float _snowPhase;

    private void Awake()
    {
        if (标题故障 != null && 标题故障材质 != null)
        {
            _rgb = new Material(标题故障材质);
            标题故障.material = _rgb;
        }

        if (雪花 != null && 雪花材质 != null)
        {
            _snow = new Material(雪花材质);
            雪花.material = _snow;
        }

        Apply();
    }

    private void Update()
    {
        float k = Mathf.Clamp01(m_GlitchAmount);
        float fast = k * k;

        // 速率用累积步进，避免“可变时间 × 频率”造成跳变
        _rgbStep += Time.deltaTime * Mathf.Lerp(8f, 70f, fast);
        _snowPhase += Time.deltaTime * Mathf.Lerp(0.7f, 2.6f, fast);

        Apply();
    }

    private void Apply()
    {
        float k = Mathf.Clamp01(m_GlitchAmount);
        float fast = k * k;

        if (_rgb != null)
        {
            _rgb.SetFloat("_Phase", Mathf.Floor(_rgbStep));
            
            // 基础的错位偏移量，随着故障强度变大而变大
            float currentShift = Mathf.Lerp(0.003f, 0.025f, k);

            // 【核心调整：三原色分离】
            // 红色往左偏（负值），蓝色往右偏（正值），中间露出绿色
            // 这样就能拉开距离，避免红蓝重叠成紫红色
            _rgb.SetFloat("_RedShift", -currentShift * 0.8f); 
            _rgb.SetFloat("_BlueShift", currentShift * 1.2f); 
            
            // 兼容旧的 _Shift 参数（防止旧Shader报错）
            _rgb.SetFloat("_Shift", currentShift);

            _rgb.SetFloat("_TearChance", Mathf.Lerp(0.01f, 0.45f, fast));
            _rgb.SetFloat("_TearAmount", Mathf.Lerp(0.004f, 0.05f, k));
        }

        if (_snow != null)
        {
            _snow.SetFloat("_Phase", _snowPhase);
            _snow.SetFloat("_Density", Mathf.Lerp(560f, 780f, k));
            _snow.SetFloat("_Threshold", Mathf.Lerp(0.97f, 0.86f, k));
            _snow.SetFloat("_BandAmount", Mathf.Lerp(0.03f, 0.16f, fast));
        }
    }
}