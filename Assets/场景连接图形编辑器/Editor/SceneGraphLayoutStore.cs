using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Lament.SceneGraph
{
    /// <summary>
    /// Persists card positions and the mini map origin as a small JSON file.
    /// </summary>
    public static class SceneGraphLayoutStore
    {
        private const string Folder = "Assets/场景连接图形编辑器";
        private const string FileName = "SceneGraphLayout.json";

        public static string FilePath =>
            Path.Combine(ProjectRoot, Folder.Replace('/', Path.DirectorySeparatorChar), FileName);

        public static string ProjectRoot =>
            Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;

        public static SceneGraphLayoutData Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new SceneGraphLayoutData();
                return JsonUtility.FromJson<SceneGraphLayoutData>(File.ReadAllText(FilePath))
                       ?? new SceneGraphLayoutData();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SceneGraph] 布局读取失败：" + ex.Message);
                return new SceneGraphLayoutData();
            }
        }

        public static void Save(SceneGraphLayoutData data)
        {
            if (data == null) return;
            try
            {
                string directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(FilePath, JsonUtility.ToJson(data, true));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SceneGraph] 布局保存失败：" + ex.Message);
            }
        }

        public static void Apply(List<SceneGraphScene> scenes, SceneGraphLayoutData data)
        {
            if (scenes == null || data == null) return;

            var map = new Dictionary<string, SceneGraphNodeLayout>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in data.nodes)
                if (node != null && !string.IsNullOrEmpty(node.scenePath))
                    map[node.scenePath] = node;

            foreach (var scene in scenes)
            {
                if (!map.TryGetValue(scene.scenePath, out var node)) continue;
                scene.x = node.x;
                scene.y = node.y;
                scene.collapsed = node.collapsed;
            }
        }

        public static void Capture(List<SceneGraphScene> scenes, SceneGraphLayoutData data)
        {
            if (scenes == null || data == null) return;
            data.nodes.Clear();
            foreach (var scene in scenes)
                data.nodes.Add(new SceneGraphNodeLayout
                {
                    scenePath = scene.scenePath,
                    x = scene.x,
                    y = scene.y,
                    collapsed = scene.collapsed
                });
        }
    }
}
