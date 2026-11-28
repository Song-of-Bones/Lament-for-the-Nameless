using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Lament.SceneGraph
{
    /// <summary>
    /// Turns raw scan data into actionable diagnostics: missing targets, IDs that are not
    /// in the database, duplicate IDs, orphan window definitions and jump cycles.
    /// </summary>
    public static class SceneGraphValidator
    {
        public static void Validate(List<SceneGraphScene> scenes, EditorBuildSettingsScene[] build)
        {
            if (scenes == null) return;

            var buildPaths = new HashSet<string>(
                (build ?? Array.Empty<EditorBuildSettingsScene>())
                    .Where(scene => scene != null && scene.enabled)
                    .Select(scene => scene.path),
                StringComparer.OrdinalIgnoreCase);

            foreach (var scene in scenes)
            {
                scene.diagnostics.Clear();

                var duplicateGroups = scene.windowDefs
                    .GroupBy(def => (def.isJump, def.id))
                    .Where(group => !string.IsNullOrEmpty(group.Key.id) && group.Count() > 1);

                foreach (var group in duplicateGroups)
                    scene.diagnostics.Add(new SceneGraphDiagnostic
                    {
                        level = "warning",
                        message = $"ID \"{group.Key.id}\" 在{(group.Key.isJump ? "跳转" : "异步")}列表中重复 {group.Count()} 次",
                        scenePath = scene.scenePath
                    });

                foreach (var source in scene.sources)
                {
                    if (string.IsNullOrEmpty(source.targetSceneName)) continue;

                    if (string.IsNullOrEmpty(source.targetScenePath))
                    {
                        scene.diagnostics.Add(new SceneGraphDiagnostic
                        {
                            level = "error",
                            message = $"{source.displayName} 的目标场景 \"{source.targetSceneName}\" 不存在",
                            scenePath = scene.scenePath,
                            objectPath = source.objectPath
                        });
                    }
                    else if (!buildPaths.Contains(source.targetScenePath))
                    {
                        scene.diagnostics.Add(new SceneGraphDiagnostic
                        {
                            level = "warning",
                            message = $"{source.displayName} 的目标场景 \"{source.targetSceneName}\" 不在 Build Settings",
                            scenePath = scene.scenePath,
                            objectPath = source.objectPath
                        });
                    }
                }

                foreach (var def in scene.windowDefs)
                {
                    if (string.IsNullOrEmpty(def.sceneName))
                    {
                        scene.diagnostics.Add(new SceneGraphDiagnostic
                        {
                            level = "error",
                            message = $"窗口配置 \"{def.id}\" 没有指定场景",
                            scenePath = scene.scenePath,
                            objectPath = def.objectPath
                        });
                    }
                    else if (string.IsNullOrEmpty(def.targetScenePath))
                    {
                        scene.diagnostics.Add(new SceneGraphDiagnostic
                        {
                            level = "error",
                            message = $"窗口配置 \"{def.id}\" 的场景 \"{def.sceneName}\" 不存在",
                            scenePath = scene.scenePath,
                            objectPath = def.objectPath
                        });
                    }
                    else if (!buildPaths.Contains(def.targetScenePath))
                    {
                        scene.diagnostics.Add(new SceneGraphDiagnostic
                        {
                            level = "warning",
                            message = $"窗口配置 \"{def.id}\" 的场景 \"{def.sceneName}\" 不在 Build Settings",
                            scenePath = scene.scenePath,
                            objectPath = def.objectPath
                        });
                    }

                    bool used = scene.sources.Any(source =>
                        source.kind == SceneGraphSourceKinds.MenuButton &&
                        string.Equals(source.windowId, def.id, StringComparison.Ordinal) &&
                        source.isJump == def.isJump);

                    if (!used)
                    {
                        scene.diagnostics.Add(new SceneGraphDiagnostic
                        {
                            level = "info",
                            message = $"窗口配置 \"{def.id}\"（{(def.isJump ? "跳转" : "异步")}）没有按钮引用",
                            scenePath = scene.scenePath,
                            objectPath = def.objectPath
                        });
                    }
                }
            }

            DetectCycles(scenes);
        }

        private static void DetectCycles(List<SceneGraphScene> scenes)
        {
            var edges = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var scene in scenes)
            {
                var targets = new List<string>();
                foreach (var source in scene.sources)
                {
                    if (source.mode != SceneGraphModes.Jump && source.mode != SceneGraphModes.Direct) continue;
                    if (string.IsNullOrEmpty(source.targetScenePath)) continue;
                    targets.Add(source.targetScenePath);
                }
                foreach (var def in scene.windowDefs)
                {
                    if (!def.isJump || string.IsNullOrEmpty(def.targetScenePath)) continue;
                    targets.Add(def.targetScenePath);
                }
                edges[scene.scenePath] = targets.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }

            var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var scenePath in edges.Keys)
                Visit(scenePath, edges, visiting, visited, scenes);
        }

        private static void Visit(string scenePath, Dictionary<string, List<string>> edges,
            HashSet<string> visiting, HashSet<string> visited, List<SceneGraphScene> scenes)
        {
            if (visited.Contains(scenePath)) return;
            if (!visiting.Add(scenePath))
            {
                var scene = scenes.FirstOrDefault(item =>
                    string.Equals(item.scenePath, scenePath, StringComparison.OrdinalIgnoreCase));
                scene?.diagnostics.Add(new SceneGraphDiagnostic
                {
                    level = "warning",
                    message = "检测到跳转环路，运行时可能无法返回",
                    scenePath = scenePath
                });
                return;
            }

            if (edges.TryGetValue(scenePath, out var targets))
                foreach (var target in targets)
                    Visit(target, edges, visiting, visited, scenes);

            visiting.Remove(scenePath);
            visited.Add(scenePath);
        }
    }
}
