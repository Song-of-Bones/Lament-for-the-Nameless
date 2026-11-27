using System;
using System.Collections.Generic;
using System.Linq;
using Lament.VisualNovel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ProjectRenameFixup
{
    private static readonly Dictionary<string, string> SceneNames = new Dictionary<string, string>
    {
        { "登录场景", "Login" },
        { "游戏大厅", "Lobby" },
        { "设置_异步窗口", "Settings" },
        { "视觉小说运行场景", "NovelRuntime" },
    };

    [MenuItem("工具/项目/修复英文化后的场景引用")]
    public static void Fix()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先停止播放");
        FixBuildSettings();
        foreach (var guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) continue;
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            bool changed = false;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var database in root.GetComponentsInChildren<WindowDatabase>(true))
                {
                    changed |= FixList(database.configs);
                    changed |= FixList(database.jumpConfigs);
                    if (changed) EditorUtility.SetDirty(database);
                }
                foreach (var transition in root.GetComponentsInChildren<AnimSceneTransition>(true))
                {
                    var serialized = new SerializedObject(transition);
                    var property = serialized.FindProperty("目标场景");
                    if (property != null && SceneNames.TryGetValue(property.stringValue, out var targetName))
                    {
                        property.stringValue = targetName;
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                        changed = true;
                    }
                }
                foreach (var entry in root.GetComponentsInChildren<NovelEntryPoint>(true))
                {
                    var serialized = new SerializedObject(entry);
                    var property = serialized.FindProperty("runtimeScene");
                    if (property != null && SceneNames.TryGetValue(property.stringValue, out var newName))
                    {
                        property.stringValue = newName;
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                        changed = true;
                    }
                }
            }
            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[RenameFixup] 已修复场景引用：" + path);
            }
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[RenameFixup] 完成");
    }

    private static bool FixList(List<WindowDatabase.WindowConfig> list)
    {
        bool changed = false;
        foreach (var config in list)
        {
            if (config == null) continue;
            if (SceneNames.TryGetValue(config.sceneName, out var newName))
            {
                config.sceneName = newName;
                changed = true;
            }
        }
        return changed;
    }

    private static void FixBuildSettings()
    {
        var scenes = EditorBuildSettings.scenes;
        bool changed = false;
        for (int i = 0; i < scenes.Length; i++)
        {
            var current = scenes[i];
            if (string.IsNullOrEmpty(current.guid.ToString())) continue;
            var path = AssetDatabase.GUIDToAssetPath(current.guid.ToString());
            if (string.IsNullOrEmpty(path) || path == current.path) continue;
            scenes[i] = new EditorBuildSettingsScene(path, current.enabled);
            changed = true;
        }
        if (changed) EditorBuildSettings.scenes = scenes;
        Debug.Log("[RenameFixup] Build Settings 已按 GUID 重建路径=" + changed);
    }
}
