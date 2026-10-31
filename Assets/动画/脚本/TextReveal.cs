using TMPro;
using UnityEngine;

/// <summary>
/// 文本缓显组件（仅运行时生效）。
/// 显示方式：从左到右缓显 / 从右到左缓显 / 整段缓显。
/// 入场动画：滑入渐显（从下方滑入并渐显）/ 打字机（逐字直接出现）。
/// 勾选「以同样效果消失」后，退场方向与入场相反：
/// 例如从左到右缓显入场，则从右到左缓显退场。
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("文本/文本缓显 (TextReveal)")]
public class TextReveal : MonoBehaviour
{
    public enum RevealMode
    {
        从左到右缓显,
        从右到左缓显,
        整段缓显,
    }

    public enum EntranceStyle
    {
        滑入渐显,
        打字机,
    }

    private enum State
    {
        静止,
        显示中,
        消失中,
    }

    [Header("文本")]
    [Tooltip("要控制的 TextMeshPro 文本组件")]
    [SerializeField] private TMP_Text 文本组件;

    [Header("显示")]
    [Tooltip("显示方式：从左到右缓显 / 从右到左缓显 / 整段缓显")]
    [SerializeField] private RevealMode 显示方式 = RevealMode.从左到右缓显;

    [Tooltip("入场动画：滑入渐显（从下方滑入并渐显）/ 打字机（逐字直接出现）")]
    [SerializeField] private EntranceStyle 入场动画 = EntranceStyle.打字机;

    [Tooltip("显示速度，单位：字/秒")]
    [SerializeField, Min(0.01f)] private float 显示速度 = 30f;

    [Tooltip("单个文字完成滑入渐显所需时间，仅在入场动画为滑入渐显时生效")]
    [SerializeField, Min(0f)] private float 滑入时长 = 0.25f;

    [Tooltip("文字滑入的垂直距离，仅在入场动画为滑入渐显时生效")]
    [SerializeField, Min(0f)] private float 滑入距离 = 40f;

    [Header("消失")]
    [Tooltip("勾选后文字以同样效果反向消失：从左到右缓显入场则从右到左缓显退场；不勾选则立即消失")]
    [SerializeField] private bool 以同样效果消失 = true;

    [Tooltip("消失速度，单位：字/秒，仅在勾选「以同样效果消失」时生效")]
    [SerializeField, Min(0.01f)] private float 消失速度 = 30f;

    private State _state = State.静止;
    private float _time;
    private float[] _alpha0 = new float[0];
    private float[] _lastAlpha = new float[0];
    private Vector3[][] _baseVerts;
    private Color32[][] _baseColors;
    private string _capturedText;

    /// <summary>重新播放入场效果。</summary>
    [ContextMenu("显示")]
    public void Show()
    {
        if (!Application.isPlaying || !EnsureBase()) return;

        if (_state == State.消失中)
        {
            // 从当前淡出进度平滑转回显示，避免文字闪跳
            _alpha0 = (float[])_lastAlpha.Clone();
        }
        else
        {
            _alpha0 = new float[_lastAlpha.Length]; // 全部从 0 开始显示
        }

        _time = 0f;
        _state = State.显示中;
        ApplyFrame();
    }

    /// <summary>播放消失效果；未勾选「以同样效果消失」时立即消失。</summary>
    [ContextMenu("消失")]
    public void Hide()
    {
        if (!Application.isPlaying || !EnsureBase()) return;

        if (!以同样效果消失)
        {
            WriteBase(0f);
            _state = State.静止;
            return;
        }

        _alpha0 = (float[])_lastAlpha.Clone();
        _time = 0f;
        _state = State.消失中;
        ApplyFrame();
    }

    private void Update()
    {
        if (_state == State.静止) return;

        _time += Time.deltaTime;
        ApplyFrame();
    }

    private void ApplyFrame()
    {
        var info = 文本组件.textInfo;

        // 文本在动画过程中被修改时，重新捕获基准顶点
        if (_capturedText != 文本组件.text || info.characterCount != _lastAlpha.Length)
            CaptureBase();

        int count = info.characterCount;
        bool entering = _state == State.显示中;
        bool leftToRight = 显示方式 == RevealMode.从左到右缓显;
        bool whole = 显示方式 == RevealMode.整段缓显;
        bool slide = 入场动画 == EntranceStyle.滑入渐显;
        float speed = entering ? 显示速度 : 消失速度;
        float wholeDuration = count / speed;
        float maxEnd = 0f;

        for (int i = 0; i < count; i++)
        {
            // 入场按所选方向推进，退场方向与入场相反
            int order = (leftToRight == entering) ? i : count - 1 - i;
            float start = whole ? 0f : order / speed;
            float duration = whole ? wholeDuration : (slide ? 滑入时长 : 0f);

            float p;
            if (duration <= 0f)
                p = _time >= start ? 1f : 0f;
            else
                p = Mathf.Clamp01((_time - start) / duration);

            float a0 = i < _alpha0.Length ? _alpha0[i] : (entering ? 0f : 1f);
            float a = entering ? a0 + (1f - a0) * p : a0 * (1f - p);
            _lastAlpha[i] = a;
            if (start + duration > maxEnd) maxEnd = start + duration;

            float y = slide ? (a - 1f) * 滑入距离 : 0f;

            var character = info.characterInfo[i];
            int materialIndex = character.materialReferenceIndex;
            int vertexIndex = character.vertexIndex;
            if (materialIndex >= _baseVerts.Length) continue;

            var baseColors = _baseColors[materialIndex];
            var baseVerts = _baseVerts[materialIndex];
            var colors = info.meshInfo[materialIndex].colors32;
            var verts = info.meshInfo[materialIndex].vertices;
            if (vertexIndex + 3 >= baseVerts.Length || vertexIndex + 3 >= baseColors.Length) continue;
            if (colors.Length != baseColors.Length || verts.Length != baseVerts.Length) continue;

            for (int k = 0; k < 4; k++)
            {
                Color32 color = baseColors[vertexIndex + k];
                color.a = (byte)(color.a * a);
                colors[vertexIndex + k] = color;

                Vector3 vert = baseVerts[vertexIndex + k];
                vert.y += y;
                verts[vertexIndex + k] = vert;
            }
        }

        文本组件.UpdateVertexData(TMP_VertexDataUpdateFlags.All);

        if (_time >= maxEnd)
            _state = State.静止;
    }

    /// <summary>组件启用时自动查找文本组件并触发表现。</summary>
    private void OnEnable()
    {
        // 如果未在Inspector中手动赋值，则自动查找同对象上的TMP_Text
        if (文本组件 == null)
        {
            文本组件 = GetComponent<TMP_Text>();
        }

        // 自动触发表现
        Show();
    }

    private bool EnsureBase()
    {
        if (文本组件 == null)
        {
            Debug.LogWarning("[TextReveal] 未指定文本组件。", this);
            return false;
        }

        if (_baseVerts == null || _capturedText != 文本组件.text)
            CaptureBase();

        return true;
    }

    private void CaptureBase()
    {
        文本组件.ForceMeshUpdate();

        var info = 文本组件.textInfo;
        _baseVerts = new Vector3[info.meshInfo.Length][];
        _baseColors = new Color32[info.meshInfo.Length][];
        for (int m = 0; m < info.meshInfo.Length; m++)
        {
            _baseVerts[m] = (Vector3[])info.meshInfo[m].vertices.Clone();
            _baseColors[m] = (Color32[])info.meshInfo[m].colors32.Clone();
        }

        _capturedText = 文本组件.text;

        var oldLast = _lastAlpha;
        _lastAlpha = new float[info.characterCount];
        for (int i = 0; i < _lastAlpha.Length; i++)
            _lastAlpha[i] = i < oldLast.Length ? oldLast[i] : 1f;
    }

    /// <summary>恢复基准顶点并整体设置透明度（用于立即消失）。</summary>
    private void WriteBase(float alpha)
    {
        var info = 文本组件.textInfo;
        for (int m = 0; m < info.meshInfo.Length && m < _baseColors.Length; m++)
        {
            var baseColors = _baseColors[m];
            var baseVerts = _baseVerts[m];
            var colors = info.meshInfo[m].colors32;
            var verts = info.meshInfo[m].vertices;
            int n = Mathf.Min(baseColors.Length, colors.Length);
            int nv = Mathf.Min(baseVerts.Length, verts.Length);

            for (int i = 0; i < n; i++)
            {
                Color32 color = baseColors[i];
                color.a = (byte)(color.a * alpha);
                colors[i] = color;
            }
            for (int i = 0; i < nv; i++)
                verts[i] = baseVerts[i];
        }

        for (int i = 0; i < _lastAlpha.Length; i++)
            _lastAlpha[i] = alpha;

        文本组件.UpdateVertexData(TMP_VertexDataUpdateFlags.All);
    }
}