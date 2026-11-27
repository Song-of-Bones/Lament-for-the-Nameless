using System;
using System.IO;
using Lament.VisualNovel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class NovelPreviewTest
{
    private const string Prefix = "Lament.VisualNovel.PreviewTest.";
    private static double deadline;
    private static string result;

    static NovelPreviewTest()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (!SessionState.GetBool(Prefix + "Active", false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                result = "";
                deadline = EditorApplication.timeSinceStartup + 30;
                EditorApplication.update += Observe;
            }
            if (state == PlayModeStateChange.ExitingPlayMode) EditorApplication.update -= Observe;
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += VerifyRestored;
        };
    }

    [MenuItem("工具/视觉小说/运行节点预览测试")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先停止播放");
        var story = AssetDatabase.LoadAssetAtPath<NovelStoryAsset>(NovelProjectSetup.StoryPath);
        if (story == null || story.FindNode("route-a") == null) throw new InvalidOperationException("缺少可预览的示例剧情");
        result = "";
        SessionState.SetString(Prefix + "OriginalEntry", story.entryNode.nodeId);
        SessionState.SetString(Prefix + "OriginalScene", SceneManager.GetActiveScene().path);
        SessionState.SetString(Prefix + "OriginalStartScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(Prefix + "Observed", false);
        SessionState.SetBool(Prefix + "Active", true);
        try
        {
            NovelStoryPreview.Play(story, story.FindNode("route-a"));
            if (!EditorApplication.isPlayingOrWillChangePlaymode) SessionState.SetBool(Prefix + "Active", false);
        }
        catch { SessionState.SetBool(Prefix + "Active", false); throw; }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        result += "PASS " + message + "\n";
    }

    private static void Observe()
    {
        try
        {
            var player = UnityEngine.Object.FindAnyObjectByType<NovelPlayer>();
            if (player == null || player.Session == null || player.Session.Current == null || player.Session.Current.nodeId != "route-a")
            {
                if (EditorApplication.timeSinceStartup < deadline) return;
                throw new TimeoutException("节点预览未进入目标节点");
            }
            Check(player.Session.Story.storyId == "framework-demo-prologue", "预览使用指定剧情");
            Check(player.Session.Current.nodeId == "route-a", "预览直接进入选中节点");
            Check(player.Session.Variables.Get("route").integer == 0, "预览使用默认变量而非推演前置路径");
            Check(player.Session.State.history.Count == 1, "预览不保留起始场景的临时历史");
            Check(player.Session.Story.entryNode.nodeId == SessionState.GetString(Prefix + "OriginalEntry", ""), "预览未改写资源入口");
            var view = UnityEngine.Object.FindAnyObjectByType<NovelPresentationController>();
            Check(view != null && view.body.text == player.Session.Current.text, "预览正文绑定目标节点");
            SessionState.SetBool(Prefix + "Observed", true);
        }
        catch (Exception ex) { result += "FAIL " + ex + "\n"; }
        EditorApplication.update -= Observe;
        SessionState.SetString(Prefix + "Result", result);
        EditorApplication.ExitPlaymode();
    }

    private static void VerifyRestored()
    {
        result = SessionState.GetString(Prefix + "Result", "");
        try
        {
            Check(SessionState.GetBool(Prefix + "Observed", false), "预览播放检查完成");
            Check(SceneManager.GetActiveScene().path == SessionState.GetString(Prefix + "OriginalScene", ""), "停止预览后恢复原编辑场景");
            Check(AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) == SessionState.GetString(Prefix + "OriginalStartScene", ""), "停止预览后恢复原播放起始场景设置");
        }
        catch (Exception ex) { result += "FAIL " + ex + "\n"; }
        SessionState.SetBool(Prefix + "Active", false);
        Directory.CreateDirectory(".codely-cli/test-results");
        File.WriteAllText(".codely-cli/test-results/visual-novel-preview.txt", result);
        if (result.Contains("FAIL")) Debug.LogError("[VisualNovel] 节点预览测试失败\n" + result);
        else Debug.Log("[VisualNovel] 节点预览测试全部通过\n" + result);
    }
}
