using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

/// <summary>
/// Android 包名设置入口：
/// 菜单会打开窗口，显示当前包名并允许输入新包名；
/// 命令行仍可用 -executeMethod SecurityCli.SetAndroidPackageName 应用默认包名。
/// </summary>
public static class SecurityCli
{
    public const string AndroidPackageName = "com.Lament.for.the.Nameless.xie.chen";

    [MenuItem("防逆向/设置 Android 包名", false, 20)]
    public static void 打开包名设置窗口()
    {
        安卓包名设置窗口.打开();
    }

    /// <summary>命令行入口：Unity.exe -executeMethod SecurityCli.SetAndroidPackageName</summary>
    public static void SetAndroidPackageName()
    {
        if (应用包名(AndroidPackageName))
            Debug.Log("[防逆向] Android 包名已设置为：" + AndroidPackageName);
    }

    internal static bool 应用包名(string 包名)
    {
        包名 = (包名 ?? string.Empty).Trim();

        string 错误;
        if (!安卓包名设置窗口.验证包名(包名, out 错误))
        {
            Debug.LogError("[防逆向] 包名无效：" + 错误);
            return false;
        }

        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, 包名);
        AssetDatabase.SaveAssets();
        return true;
    }
}

/// <summary>Android 包名设置窗口：显示当前 Application Id，输入新值后应用。</summary>
public class 安卓包名设置窗口 : EditorWindow
{
    private string _包名 = string.Empty;
    private string _错误;
    private bool _已聚焦;

    public static void 打开()
    {
        var 窗口 = GetWindow<安卓包名设置窗口>(true, "设置 Android 包名", true);
        窗口.minSize = new Vector2(480f, 200f);
        窗口._包名 = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
        窗口._错误 = null;
        窗口._已聚焦 = false;
        窗口.ShowModal();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space();

        EditorGUILayout.LabelField("当前包名", EditorStyles.boldLabel);
        string 当前 = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
        EditorGUILayout.SelectableLabel(
            string.IsNullOrEmpty(当前) ? "（未设置）" : 当前,
            EditorStyles.textField,
            GUILayout.Height(EditorGUIUtility.singleLineHeight));

        EditorGUILayout.Space();

        GUI.SetNextControlName("包名输入框");
        _包名 = EditorGUILayout.TextField("新包名", _包名);
        if (!_已聚焦)
        {
            _已聚焦 = true;
            EditorGUI.FocusTextInControl("包名输入框");
        }

        if (!string.IsNullOrEmpty(_错误))
            EditorGUILayout.HelpBox(_错误, MessageType.Warning);

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("取消", GUILayout.Width(80f))) Close();
            if (GUILayout.Button("应用", GUILayout.Width(80f))) 应用();
        }

        if (Event.current.type == EventType.KeyDown &&
            (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
        {
            应用();
            Event.current.Use();
        }
    }

    private void 应用()
    {
        string 错误;
        if (!验证包名(_包名, out 错误))
        {
            _错误 = 错误;
            Repaint();
            return;
        }

        _错误 = null;
        if (SecurityCli.应用包名(_包名))
        {
            Debug.Log("[防逆向] Android 包名已设置为：" + _包名.Trim());
            Close();
        }
    }

    /// <summary>校验 Android Application Id：至少两段、每段以英文字母开头、只含字母数字下划线。</summary>
    public static bool 验证包名(string 包名, out string 错误)
    {
        错误 = null;
        包名 = (包名 ?? string.Empty).Trim();

        if (包名.Length == 0)
        {
            错误 = "包名不能为空。";
            return false;
        }

        if (包名.IndexOf('-') >= 0)
        {
            错误 = "包名不能包含连字符“-”，请改用下划线。";
            return false;
        }

        string[] 段 = 包名.Split('.');
        if (段.Length < 2)
        {
            错误 = "Android 包名至少需要两段，例如 com.company.game。";
            return false;
        }

        for (int i = 0; i < 段.Length; i++)
        {
            string 当前段 = 段[i];
            if (当前段.Length == 0)
            {
                错误 = "包名不能包含连续的点，也不能以点开头或结尾。";
                return false;
            }

            if (!是英文字母(当前段[0]))
            {
                错误 = "包名每一段都必须以英文字母开头：" + 当前段;
                return false;
            }

            for (int j = 0; j < 当前段.Length; j++)
            {
                char 字符 = 当前段[j];
                if (!是英文字母(字符) && !是数字(字符) && 字符 != '_')
                {
                    错误 = "包名只能包含英文字母、数字和下划线，非法字符：" + 字符;
                    return false;
                }
            }
        }

        return true;
    }

    private static bool 是英文字母(char 字符)
    {
        return (字符 >= 'a' && 字符 <= 'z') || (字符 >= 'A' && 字符 <= 'Z');
    }

    private static bool 是数字(char 字符)
    {
        return 字符 >= '0' && 字符 <= '9';
    }
}
