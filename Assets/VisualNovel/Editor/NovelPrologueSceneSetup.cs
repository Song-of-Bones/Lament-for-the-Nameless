using System;
using Lament.VisualNovel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class NovelPrologueSceneSetup
{
    public const string ScenePath = "Assets/VisualNovel/Scenes/NovelRuntime.unity";

    [MenuItem("工具/视觉小说/把序章接入运行场景")]
    public static void Assign()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("先停止播放");
        var story = AssetDatabase.LoadAssetAtPath<NovelStoryAsset>(NovelPrologueBuilder.StoryPath);
        if (story == null) throw new InvalidOperationException("请先生成序章剧情");
        var errors = NovelStoryValidator.Validate(story);
        if (errors.Count > 0) throw new InvalidOperationException("序章校验失败：\n" + string.Join("\n", errors));

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var player = UnityEngine.Object.FindAnyObjectByType<NovelPlayer>();
        if (player == null) throw new InvalidOperationException("运行场景缺少 NovelPlayer");
        var serialized = new SerializedObject(player);
        var storyProperty = serialized.FindProperty("story");
        if (storyProperty == null) throw new InvalidOperationException("NovelPlayer 缺少 story 字段");
        storyProperty.objectReferenceValue = story;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[VisualNovel] 序章已接入运行场景：" + NovelPrologueBuilder.StoryPath);
    }
}
