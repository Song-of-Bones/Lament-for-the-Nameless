using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 把设置里的 Toggle 绑定到 <see cref="ResolutionAdapt"/>：
/// 打开面板时同步当前状态，切换时写入全局设置（大厅背景会立即响应）。
/// </summary>
[RequireComponent(typeof(Toggle))]
[DisallowMultipleComponent]
[AddComponentMenu("UI/分辨率自适应开关 (ResolutionAdaptToggle)")]
public class ResolutionAdaptToggle : MonoBehaviour
{
    private Toggle _toggle;

    private void Awake()
    {
        _toggle = GetComponent<Toggle>();
        _toggle.SetIsOnWithoutNotify(ResolutionAdapt.已开启);
        _toggle.onValueChanged.AddListener(OnValueChanged);
    }

    private void OnDestroy()
    {
        if (_toggle != null) _toggle.onValueChanged.RemoveListener(OnValueChanged);
    }

    private void OnValueChanged(bool value)
    {
        ResolutionAdapt.已开启 = value;
    }
}
