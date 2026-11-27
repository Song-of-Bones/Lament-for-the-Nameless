using System;
using System.IO;
using Lament.VisualNovel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class SettingsPageTest
{
    private const string Key = "Lament.VisualNovel.SettingsTest";
    private static double started;
    private static int phase;
    private static string result = "";

    static SettingsPageTest() { EditorApplication.update += Poll; }

    [MenuItem("工具/项目/运行设置页检查")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("先停止播放");
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        result = "";
        phase = 0;
        started = 0;
        SessionState.SetBool(Key, true);
        var scene = AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>("Assets/Scenes/Settings.unity");
        EditorSceneManager.playModeStartScene = scene;
        EditorApplication.EnterPlaymode();
    }

    private static void Poll()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) return;
        if (started == 0) { started = EditorApplication.timeSinceStartup; return; }
        var elapsed = EditorApplication.timeSinceStartup - started;
        try
        {
            switch (phase)
            {
                case 0:
                    if (elapsed < .5) return;
                    var window = UnityEngine.Object.FindAnyObjectByType<SettingsWindow>();
                    if (window == null) { Fail("找不到 SettingsWindow"); return; }
                    window.打开(null);
                    phase = 1;
                    return;
                case 1:
                    if (elapsed < 2) return;
                    var panel = UnityEngine.Object.FindAnyObjectByType<NovelSettingsPanel>();
                    if (panel == null) { Fail("找不到 NovelSettingsPanel"); return; }
                    Directory.CreateDirectory(".codely-cli/test-results");
                    ScreenCapture.CaptureScreenshot(".codely-cli/test-results/settings-page.png");
                    phase = 2;
                    return;
                case 2:
                    if (elapsed < 2.6) return;
                    var speed = GameObject.Find("Canvas/面板/滚动区/视口/内容/Setting_TextSpeed/Slider")?.GetComponent<UnityEngine.UI.Slider>();
                    var music = GameObject.Find("Canvas/面板/滚动区/视口/内容/Setting_Music/Slider")?.GetComponent<UnityEngine.UI.Slider>();
                    if (speed == null || music == null) { Fail("找不到设置滑块"); return; }
                    speed.value = 90f;
                    music.value = .25f;
                    phase = 3;
                    return;
                case 3:
                    if (elapsed < 3.2) return;
                    Check(Mathf.Approximately(GameSettings.TextSpeed, 90f), "文字速度写入设置：" + GameSettings.TextSpeed);
                    Check(Mathf.Approximately(GameSettings.MusicVolume, .25f), "音乐音量写入设置：" + GameSettings.MusicVolume);
                    Check(Mathf.Approximately(PlayerPrefs.GetFloat("novel_text_speed", -1), 90f), "文字速度已持久化到 PlayerPrefs");
                    var label = GameObject.Find("Canvas/面板/滚动区/视口/内容/Setting_TextSpeed/Value")?.GetComponent<TMPro.TMP_Text>();
                    Check(label != null && label.text.Contains("90"), "数值标签实时更新：" + (label?.text ?? "null"));
                    // Restore defaults so the test does not leave user preferences changed.
                    GameSettings.ResetToDefaults();
                    GameSettings.Flush();
                    Check(Mathf.Approximately(GameSettings.TextSpeed, GameSettings.DefaultTextSpeed), "恢复默认设置生效");
                    Finish(true);
                    return;
            }
        }
        catch (Exception ex) { Fail(ex.ToString()); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        result += "PASS " + message + "\n";
    }

    private static void Fail(string message)
    {
        result += "FAIL " + message + "\n";
        Finish(false);
    }

    private static void Finish(bool passed)
    {
        SessionState.SetBool(Key, false);
        Directory.CreateDirectory(".codely-cli/test-results");
        File.WriteAllText(".codely-cli/test-results/settings-page.txt", result);
        if (passed) Debug.Log("[Settings] 设置页检查通过\n" + result);
        else Debug.LogError("[Settings] 设置页检查失败\n" + result);
        EditorSceneManager.playModeStartScene = null;
        EditorApplication.ExitPlaymode();
    }
}
