using System;
using Lament.VisualNovel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class NovelStoryPreview
{
    private const string Prefix = "Lament.VisualNovel.Preview.";
    private static double deadline;

    static NovelStoryPreview()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    public static void Play(NovelStoryAsset story, NovelNodeAsset entry)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("请在编辑模式且编译完成后预览");
        var errors = NovelStoryValidator.Validate(story);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
        if (entry != null && !story.nodes.Contains(entry)) throw new InvalidOperationException("预览节点不属于当前剧情");
        string path = AssetDatabase.GetAssetPath(story);
        if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("请先保存剧情资源");
        var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(NovelProjectSetup.ScenePath);
        if (scene == null) throw new InvalidOperationException("缺少 NovelRuntime 场景");
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        AssetDatabase.SaveAssets();
        SessionState.SetString(Prefix + "Story", path);
        SessionState.SetString(Prefix + "Node", entry != null ? entry.nodeId : "");
        SessionState.SetString(Prefix + "PreviousScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(Prefix + "Active", true);
        EditorSceneManager.playModeStartScene = scene;
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Prefix + "Active", false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            deadline = EditorApplication.timeSinceStartup + 30;
            EditorApplication.update -= StartPreview;
            EditorApplication.update += StartPreview;
        }
        if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.update -= StartPreview;
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Prefix + "PreviousScene", ""));
            SessionState.SetBool(Prefix + "Active", false);
        }
    }

    private static void StartPreview()
    {
        var player = UnityEngine.Object.FindAnyObjectByType<NovelPlayer>();
        if (player == null || player.Session == null)
        {
            if (EditorApplication.timeSinceStartup <= deadline) return;
            Fail("剧情预览等待播放器初始化超时");
            return;
        }
        EditorApplication.update -= StartPreview;
        try
        {
            var story = AssetDatabase.LoadAssetAtPath<NovelStoryAsset>(SessionState.GetString(Prefix + "Story", ""));
            player.StartStory(story, SessionState.GetString(Prefix + "Node", ""));
            Debug.Log("[VisualNovel] 从默认变量状态开始预览：" + story.storyId + "/" + player.Session.Current.nodeId);
        }
        catch (Exception ex) { Fail(ex.Message); }
    }

    private static void Fail(string message)
    {
        EditorApplication.update -= StartPreview;
        Debug.LogError("[VisualNovel] " + message);
        EditorApplication.ExitPlaymode();
    }
}
