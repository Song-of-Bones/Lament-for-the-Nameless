using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 资源加密工具：
/// 1. 源资源支持多选（可混合文件夹和单个文件，支持 Project 多选和拖拽）；
/// 2. 源列表为空时，递归加密“源为空时加密”指定的顶层目录（默认 Assets/Resources）；
/// 3. 输出保留文件在 Assets 下的相对路径，避免多目录同名文件互相覆盖；
/// 4. 一键完成“加密 + 生成 HMAC 完整性清单”。
/// </summary>
public class 防逆向工具窗口 : EditorWindow
{
    private const string 默认输出目录 = "Assets/StreamingAssets/EncryptedAssets";
    private const string 默认顶层目录 = "Assets/Resources";

    private readonly List<UnityEngine.Object> _源对象 = new List<UnityEngine.Object>();
    private string _默认源目录 = 默认顶层目录;
    private string _输出目录 = 默认输出目录;
    private string _扩展名过滤 = string.Empty;
    private bool _加密前清空 = true;
    private Vector2 _滚动位置;

    private sealed class 待加密文件
    {
        public string 源路径;
        public string 相对路径;
    }

    [MenuItem("防逆向/加密资源工具", false, 2)]
    private static void 打开()
    {
        var 窗口 = GetWindow<防逆向工具窗口>("资源加密");
        窗口.minSize = new Vector2(540f, 580f);
    }

    private void OnGUI()
    {
        _滚动位置 = EditorGUILayout.BeginScrollView(_滚动位置);

        byte[] 密钥 = 密钥生成器.读取主密钥();
        if (密钥 == null)
        {
            EditorGUILayout.HelpBox("尚未生成资源密钥。", MessageType.Error);
            if (GUILayout.Button("生成新密钥")) 密钥生成器.生成新密钥();
        }
        else
        {
            EditorGUILayout.HelpBox("资源密钥已就绪，指纹：" + 密钥生成器.指纹(密钥), MessageType.Info);
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("1. 选择源资源（可多选）", EditorStyles.boldLabel);

        绘制拖放区();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("添加 Project 选中项")) 添加选中项();
            using (new EditorGUI.DisabledScope(_源对象.Count == 0))
            {
                if (GUILayout.Button("清空", GUILayout.Width(64f))) _源对象.Clear();
            }
        }

        绘制源列表();

        using (new EditorGUILayout.HorizontalScope())
        {
            _默认源目录 = EditorGUILayout.TextField("源为空时加密", _默认源目录);
            if (GUILayout.Button("取选中", GUILayout.Width(64f))) 取选中目录();
        }

        string[] 顶层目录 = AssetDatabase.GetSubFolders("Assets");
        if (顶层目录.Length > 0)
        {
            int 当前索引 = Array.IndexOf(顶层目录, _默认源目录);
            int 新索引 = EditorGUILayout.Popup("快捷选择顶层目录", 当前索引, 顶层目录);
            if (新索引 >= 0 && 新索引 != 当前索引) _默认源目录 = 顶层目录[新索引];
        }

        EditorGUILayout.LabelField(" ", "列表为空时，递归加密该顶层目录（默认 Assets/Resources）。", EditorStyles.miniLabel);

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("2. 输出设置", EditorStyles.boldLabel);
        _输出目录 = EditorGUILayout.TextField("输出目录", _输出目录);
        _扩展名过滤 = EditorGUILayout.TextField("扩展名过滤", _扩展名过滤);
        EditorGUILayout.LabelField(" ", "留空表示全部；多个用分号分隔，例如 png;jpg;bundle", EditorStyles.miniLabel);
        _加密前清空 = EditorGUILayout.ToggleLeft("加密前清空输出目录中的旧 .bytes 文件", _加密前清空);
        EditorGUILayout.LabelField(" ", "输出保留源文件在 Assets 下的相对路径：美术素材/a.png → 美术素材/a.png.bytes", EditorStyles.miniLabel);

        EditorGUILayout.Space(8f);
        using (new EditorGUI.DisabledScope(密钥 == null))
        {
            if (GUILayout.Button("一键加密并生成完整性清单", GUILayout.Height(34f))) 一键处理(密钥);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("仅加密")) 仅加密(密钥);
                if (GUILayout.Button("仅生成完整性清单")) 生成清单(密钥);
            }
        }

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("3. 自检", EditorStyles.boldLabel);
        if (GUILayout.Button("检查当前配置")) 检查配置();

        EditorGUILayout.EndScrollView();
    }

    private void 绘制拖放区()
    {
        Rect 区域 = GUILayoutUtility.GetRect(0f, 58f, GUILayout.ExpandWidth(true));
        GUI.Box(区域, "把文件夹或单个素材拖到这里（支持多选）\n也可以使用下方的“添加 Project 选中项”", EditorStyles.helpBox);

        if (!区域.Contains(Event.current.mousePosition)) return;

        if (Event.current.type == EventType.DragUpdated)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            Event.current.Use();
        }
        else if (Event.current.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            UnityEngine.Object[] 对象数组 = DragAndDrop.objectReferences;
            for (int i = 0; i < 对象数组.Length; i++) 添加源(对象数组[i]);
            Event.current.Use();
            Repaint();
        }
    }

    private void 绘制源列表()
    {
        if (_源对象.Count == 0)
        {
            EditorGUILayout.HelpBox("尚未选择源。列表为空时将加密“源为空时加密”指定的顶层目录。", MessageType.None);
            return;
        }

        for (int i = 0; i < _源对象.Count; i++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                string 路径 = AssetDatabase.GetAssetPath(_源对象[i]);
                bool 是文件夹 = AssetDatabase.IsValidFolder(路径);
                EditorGUILayout.LabelField((是文件夹 ? "文件夹  " : "文件    ") + 路径);

                if (GUILayout.Button("移除", GUILayout.Width(52f)))
                {
                    _源对象.RemoveAt(i);
                    i--;
                }
            }
        }
    }

    private void 添加选中项()
    {
        UnityEngine.Object[] 选中 = Selection.objects;
        if (选中 == null || 选中.Length == 0)
        {
            EditorUtility.DisplayDialog("没有选中对象", "请先在 Project 窗口中选择文件夹或素材。", "好");
            return;
        }

        int 添加数 = 0;
        for (int i = 0; i < 选中.Length; i++)
        {
            if (添加源(选中[i])) 添加数++;
        }

        if (添加数 == 0)
            EditorUtility.DisplayDialog("没有可添加的对象", "选中的对象需要位于 Assets 目录下。", "好");

        Repaint();
    }

    private bool 添加源(UnityEngine.Object 对象)
    {
        if (对象 == null) return false;

        string 路径 = 规范化路径(AssetDatabase.GetAssetPath(对象));
        if (!路径.StartsWith("Assets/", StringComparison.Ordinal)) return false;
        if (!AssetDatabase.IsValidFolder(路径) && !File.Exists(路径)) return false;

        for (int i = 0; i < _源对象.Count; i++)
        {
            if (string.Equals(规范化路径(AssetDatabase.GetAssetPath(_源对象[i])), 路径, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        _源对象.Add(对象);
        return true;
    }

    private void 取选中目录()
    {
        if (Selection.activeObject == null) return;

        string 路径 = 规范化路径(AssetDatabase.GetAssetPath(Selection.activeObject));
        if (AssetDatabase.IsValidFolder(路径)) _默认源目录 = 路径;
    }

    private void 一键处理(byte[] 密钥)
    {
        string 错误;
        List<待加密文件> 文件 = 收集文件(out 错误);
        if (错误 != null)
        {
            EditorUtility.DisplayDialog("无法加密", 错误, "好");
            return;
        }

        if (文件 == null || 文件.Count == 0)
        {
            EditorUtility.DisplayDialog("没有可加密的文件", "没有找到匹配的文件。", "好");
            return;
        }

        string 输出 = 规范化目录(_输出目录);
        if (!确认加密(文件.Count, 输出)) return;

        if (!加密文件列表(文件, 输出, 密钥)) return;
        生成清单(密钥, 输出);
    }

    private void 仅加密(byte[] 密钥)
    {
        string 错误;
        List<待加密文件> 文件 = 收集文件(out 错误);
        if (错误 != null)
        {
            EditorUtility.DisplayDialog("无法加密", 错误, "好");
            return;
        }

        if (文件 == null || 文件.Count == 0)
        {
            EditorUtility.DisplayDialog("没有可加密的文件", "没有找到匹配的文件。", "好");
            return;
        }

        string 输出 = 规范化目录(_输出目录);
        if (!确认加密(文件.Count, 输出)) return;

        加密文件列表(文件, 输出, 密钥);
    }

    private bool 确认加密(int 数量, string 输出)
    {
        string 源说明 = _源对象.Count == 0
            ? "源：列表为空 → " + 规范化目录(_默认源目录)
            : "源：" + _源对象.Count + " 项";

        return EditorUtility.DisplayDialog(
            "确认加密",
            "将加密 " + 数量 + " 个文件。\n\n" + 源说明 + "\n输出：" + 输出 +
            (_加密前清空 ? "\n\n将先清空输出目录中的旧 .bytes 文件。" : "\n\n同名文件将被覆盖。"),
            "开始", "取消");
    }

    private List<待加密文件> 收集文件(out string 错误)
    {
        错误 = null;

        string 输出 = 规范化目录(_输出目录);
        if (!输出.StartsWith("Assets/", StringComparison.Ordinal))
        {
            错误 = "输出目录必须位于 Assets 下，例如 " + 默认输出目录 + "。";
            return null;
        }

        var 源列表 = new List<string>();

        if (_源对象.Count == 0)
        {
            string 默认 = 规范化目录(_默认源目录);
            if (string.IsNullOrEmpty(默认) || !AssetDatabase.IsValidFolder(默认))
            {
                错误 = "源列表为空，且“源为空时加密”的目录无效：" + _默认源目录 +
                       "\n\n请添加源，或填写 Assets 下的有效目录（例如 Assets/Resources）。";
                return null;
            }

            源列表.Add(默认);
        }
        else
        {
            for (int i = 0; i < _源对象.Count; i++)
            {
                string 路径 = 规范化路径(AssetDatabase.GetAssetPath(_源对象[i]));
                if (string.IsNullOrEmpty(路径)) continue;
                if (AssetDatabase.IsValidFolder(路径) || File.Exists(路径)) 源列表.Add(路径);
            }
        }

        if (源列表.Count == 0)
        {
            错误 = "没有可加密的源。";
            return null;
        }

        for (int i = 0; i < 源列表.Count; i++)
        {
            if (string.Equals(源列表[i], 输出, StringComparison.OrdinalIgnoreCase))
            {
                错误 = "输出目录不能与源目录相同：" + 输出;
                return null;
            }
        }

        string[] 过滤 = 解析过滤(_扩展名过滤);
        var 结果 = new List<待加密文件>();
        var 已添加 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < 源列表.Count; i++)
        {
            string 路径 = 源列表[i];

            if (AssetDatabase.IsValidFolder(路径))
            {
                string[] 全部文件;
                try
                {
                    全部文件 = Directory.GetFiles(路径, "*", SearchOption.AllDirectories);
                }
                catch (Exception e)
                {
                    错误 = "读取目录失败：" + 路径 + "\n" + e.Message;
                    return null;
                }

                for (int j = 0; j < 全部文件.Length; j++) 加入(全部文件[j], 输出, 过滤, 已添加, 结果);
            }
            else
            {
                加入(路径, 输出, 过滤, 已添加, 结果);
            }
        }

        return 结果;
    }

    private static void 加入(string 文件, string 输出, string[] 过滤,
        HashSet<string> 已添加, List<待加密文件> 结果)
    {
        string 规范 = 规范化路径(文件);

        if (规范.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) return;
        if (规范.StartsWith(输出 + "/", StringComparison.OrdinalIgnoreCase)) return;
        if (规范.StartsWith("Assets/防逆向工程/密钥~/", StringComparison.OrdinalIgnoreCase)) return;
        if (!匹配过滤(规范, 过滤)) return;
        if (!规范.StartsWith("Assets/", StringComparison.Ordinal)) return;

        string 相对 = 规范.Substring("Assets/".Length);
        if (!已添加.Add(相对)) return;

        结果.Add(new 待加密文件 { 源路径 = 规范, 相对路径 = 相对 });
    }

    private bool 加密文件列表(List<待加密文件> 文件, string 输出, byte[] 密钥)
    {
        if (_加密前清空) 清空旧密文(输出);

        int 成功 = 0;
        bool 取消 = false;
        string 异常 = null;

        try
        {
            for (int i = 0; i < 文件.Count; i++)
            {
                if (EditorUtility.DisplayCancelableProgressBar(
                        "加密资源", 文件[i].相对路径, (float)i / 文件.Count))
                {
                    取消 = true;
                    break;
                }

                string 目标 = Path.Combine(输出, 文件[i].相对路径 + ".bytes");
                string 目标目录 = Path.GetDirectoryName(目标);
                if (!string.IsNullOrEmpty(目标目录)) Directory.CreateDirectory(目标目录);

                byte[] 明文 = File.ReadAllBytes(文件[i].源路径);
                File.WriteAllBytes(目标, 加密文件格式.封装(明文, 密钥));
                成功++;
            }
        }
        catch (Exception e)
        {
            异常 = e.Message;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        if (异常 != null)
        {
            Debug.LogError("[防逆向] 加密失败：" + 异常);
            EditorUtility.DisplayDialog("加密失败", 异常, "好");
            return false;
        }

        AssetDatabase.Refresh();

        if (取消)
        {
            EditorUtility.DisplayDialog("已取消",
                "已取消加密，已处理 " + 成功 + " 个文件。\n输出目录可能不完整，请重新执行完整加密。", "好");
            return false;
        }

        Debug.Log("[防逆向] 已加密 " + 成功 + " 个文件到 " + 输出);
        return true;
    }

    private static void 清空旧密文(string 输出)
    {
        if (!Directory.Exists(输出)) return;

        string[] 旧文件 = Directory.GetFiles(输出, "*.bytes", SearchOption.AllDirectories);
        for (int i = 0; i < 旧文件.Length; i++)
        {
            try
            {
                File.Delete(旧文件[i]);
            }
            catch
            {
                // 单个文件删除失败不阻断整体流程
            }
        }
    }

    private void 生成清单(byte[] 密钥)
    {
        生成清单(密钥, 规范化目录(_输出目录));
    }

    private void 生成清单(byte[] 密钥, string 输出)
    {
        if (!Directory.Exists(输出))
        {
            EditorUtility.DisplayDialog("输出目录不存在", "请先加密素材，或检查输出目录：" + 输出, "好");
            return;
        }

        string[] 文件列表 = Directory.GetFiles(输出, "*.bytes", SearchOption.AllDirectories);
        var 清单行 = new List<string>();
        bool 取消 = false;

        try
        {
            for (int i = 0; i < 文件列表.Length; i++)
            {
                string 文件 = 文件列表[i];
                if (string.Equals(Path.GetFileName(文件), 安全配置.清单文件名, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (EditorUtility.DisplayCancelableProgressBar(
                        "生成完整性清单", 文件, (float)i / Mathf.Max(1, 文件列表.Length)))
                {
                    取消 = true;
                    break;
                }

                string 相对 = 规范化路径(文件).Substring(输出.Length).TrimStart('/').Replace('\\', '/');
                string 哈希 = 加密文件格式.摘要十六进制(File.ReadAllBytes(文件));
                清单行.Add(哈希 + " " + 相对);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        if (取消) return;

        if (清单行.Count == 0)
        {
            EditorUtility.DisplayDialog("没有可登记的文件", "输出目录中没有 .bytes 文件。", "好");
            return;
        }

        清单行.Sort(StringComparer.Ordinal);
        string 文本 = string.Join("\n", 清单行.ToArray()) + "\n";
        byte[] 密文 = 加密文件格式.封装(Encoding.UTF8.GetBytes(文本), 密钥);

        string 清单路径 = Path.Combine(输出, 安全配置.清单文件名);
        File.WriteAllBytes(清单路径, 密文);
        AssetDatabase.Refresh();

        Debug.Log("[防逆向] 完整性清单已生成，共 " + 清单行.Count + " 项：" + 清单路径);
        EditorUtility.DisplayDialog("完成", "完整性清单已生成，共 " + 清单行.Count + " 项。", "好");
    }

    private void 检查配置()
    {
        byte[] 密钥 = 密钥生成器.读取主密钥();
        string 输出 = 规范化目录(_输出目录);

        int 文件数 = Directory.Exists(输出)
            ? Directory.GetFiles(输出, "*.bytes", SearchOption.AllDirectories).Length
            : 0;
        bool 清单存在 = File.Exists(Path.Combine(输出, 安全配置.清单文件名));

        var sb = new StringBuilder();
        sb.AppendLine("密钥：" + (密钥 == null ? "未生成（运行时会报错）" : "已生成，指纹 " + 密钥生成器.指纹(密钥)));
        sb.AppendLine("源：" + (_源对象.Count == 0 ? "列表为空 → " + 规范化目录(_默认源目录) : _源对象.Count + " 项"));
        sb.AppendLine("输出目录：" + 输出 + (Directory.Exists(输出) ? "（存在）" : "（不存在）"));
        sb.AppendLine("加密文件数量：" + 文件数);
        sb.AppendLine("完整性清单：" + (清单存在 ? "已生成" : "缺失（运行时按配置处理）"));
        sb.AppendLine("APK 签名指纹：" + (string.IsNullOrEmpty(安全配置.期望APK签名)
            ? "未设置（Android 运行时不校验安装包签名）"
            : "已设置"));
        sb.AppendLine("异常响应方式：" + 安全配置.响应方式 + "（0 仅记录 / 1 禁用资源 / 2 退出）");

        Debug.Log("[防逆向] 配置自检\n" + sb);
        EditorUtility.DisplayDialog("配置自检", sb.ToString(), "好");
    }

    private static string 规范化目录(string 路径)
    {
        return 规范化路径(路径);
    }

    private static string 规范化路径(string 路径)
    {
        return (路径 ?? string.Empty).Replace('\\', '/').Trim().TrimEnd('/');
    }

    private static string[] 解析过滤(string 文本)
    {
        if (string.IsNullOrEmpty(文本)) return new string[0];

        string[] 原始 = 文本.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var 结果 = new List<string>();
        for (int i = 0; i < 原始.Length; i++)
        {
            string 项 = 原始[i].Trim().ToLowerInvariant();
            if (项.Length == 0) continue;
            if (!项.StartsWith(".", StringComparison.Ordinal)) 项 = "." + 项;
            结果.Add(项);
        }

        return 结果.ToArray();
    }

    private static bool 匹配过滤(string 文件, string[] 过滤)
    {
        if (过滤.Length == 0) return true;

        string 扩展名 = Path.GetExtension(文件).ToLowerInvariant();
        for (int i = 0; i < 过滤.Length; i++)
        {
            if (string.Equals(扩展名, 过滤[i], StringComparison.Ordinal)) return true;
        }

        return false;
    }
}
