using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 底部“本机设备密钥”展示：显示 6 位密钥，点击即复制到剪贴板并弹出提示。
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("UI/设备密钥 (DeviceKeyPanel)")]
public class DeviceKeyPanel : MonoBehaviour, IPointerClickHandler
{
    [Header("显示")]
    [Tooltip("6 个字符位，按顺序填入")]
    [SerializeField] private TMP_Text[] 字位;

    [Header("提示")]
    [SerializeField] private SimpleToast 提示;
    [SerializeField] private string 提示文字 = "已复制密钥";

    private string _key;

    private void Start()
    {
        刷新();
    }

    /// <summary>重新读取并刷新显示。</summary>
    public void 刷新()
    {
        _key = DeviceKey.获取();

        if (字位 == null) return;
        for (int i = 0; i < 字位.Length; i++)
        {
            if (字位[i] == null) continue;
            字位[i].text = i < _key.Length ? _key[i].ToString() : string.Empty;
        }
    }

    public void OnPointerClick(PointerEventData eventData) => 复制();

    /// <summary>复制密钥并显示右下角提示。</summary>
    public void 复制()
    {
        if (string.IsNullOrEmpty(_key)) 刷新();

        GUIUtility.systemCopyBuffer = _key;
        if (提示 != null) 提示.显示(提示文字);
    }
}
