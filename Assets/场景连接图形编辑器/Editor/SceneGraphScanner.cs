using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Lament.SceneGraph
{
    /// <summary>
    /// Reads every enabled Build Settings scene and turns it into the graph model.
    /// Scenes are opened additively for the scan and closed again; nothing is saved.
    /// </summary>
    public static class SceneGraphScanner
    {
        public static List<SceneGraphScene> Scan()
        {
            var result = new List<SceneGraphScene>();
            var build = EditorBuildSettings.scenes;
            var original = SceneManager.GetActiveScene();
            var opened = new List<Scene>();

            try
            {
                for (int i = 0; i < build.Length; i++)
                {
                    if (!build[i].enabled || string.IsNullOrEmpty(build[i].path)) continue;

                    string path = build[i].path;
                    var existing = SceneManager.GetSceneByPath(path);
                    bool own = !existing.IsValid() || !existing.isLoaded;

                    Scene scene;
                    if (own)
                    {
                        scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                        opened.Add(scene);
                    }
                    else scene = existing;

                    try { result.Add(ScanScene(scene, path, i)); }
                    catch (Exception ex)
                    {
                        result.Add(new SceneGraphScene
                        {
                            scenePath = path,
                            sceneName = Path.GetFileNameWithoutExtension(path),
                            buildIndex = i,
                            role = RoleFor(Path.GetFileNameWithoutExtension(path)),
                            diagnostics = new List<SceneGraphDiagnostic>
                            {
                                new SceneGraphDiagnostic { level = "error", message = "扫描失败：" + ex.Message, scenePath = path }
                            }
                        });
                    }
                }
            }
            finally
            {
                foreach (var scene in opened)
                    if (scene.IsValid() && scene.isLoaded)
                        EditorSceneManager.CloseScene(scene, true);

                if (original.IsValid() && original.isLoaded)
                    SceneManager.SetActiveScene(original);
            }

            SceneGraphValidator.Validate(result, build);
            return result;
        }

        public static SceneGraphScene ScanScene(Scene scene, string path, int buildIndex)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            var graph = new SceneGraphScene
            {
                scenePath = path,
                sceneName = name,
                buildIndex = buildIndex,
                role = RoleFor(name)
            };

            var behaviours = new List<MonoBehaviour>();
            foreach (var root in scene.GetRootGameObjects())
                behaviours.AddRange(root.GetComponentsInChildren<MonoBehaviour>(true));

            var database = behaviours.OfType<WindowDatabase>().FirstOrDefault();
            if (database != null)
            {
                foreach (var config in database.configs)
                {
                    if (config == null || string.IsNullOrEmpty(config.id)) continue;
                    graph.windowDefs.Add(MakeDef(database, config.id, config.sceneName, false));
                }
                foreach (var config in database.jumpConfigs)
                {
                    if (config == null || string.IsNullOrEmpty(config.id)) continue;
                    graph.windowDefs.Add(MakeDef(database, config.id, config.sceneName, true));
                }
            }

            foreach (var button in behaviours.OfType<UIMenuButton>())
            {
                var so = new SerializedObject(button);
                string id = ReadString(so, "打开窗口ID");
                bool isJump = ReadBool(so, "窗口是否为跳转");

                // UIMenuButton 的“点击结束”常接 NovelEntryPoint.StartNewGame/ContinueGame。
                // 同对象上的入口由 NovelEntryPoint 行单独表示，避免重复。
                UnityEngine.Object eventTarget = null;
                string eventMethod = null;
                var eventProperty = so.FindProperty("点击结束");
                var eventCalls = eventProperty != null
                    ? eventProperty.FindPropertyRelative("m_PersistentCalls.m_Calls")
                    : null;
                if (eventCalls != null && eventCalls.arraySize > 0)
                {
                    // UnityEvent 常带一个空的默认槽，真实目标可能在后面。
                    for (int i = 0; i < eventCalls.arraySize; i++)
                    {
                        var call = eventCalls.GetArrayElementAtIndex(i);
                        var targetRef = call.FindPropertyRelative("m_Target").objectReferenceValue;
                        if (targetRef == null) continue;
                        eventTarget = targetRef;
                        eventMethod = call.FindPropertyRelative("m_MethodName").stringValue;
                        break;
                    }
                }

                if (eventTarget is NovelEntryPoint sameEntry && sameEntry.gameObject == button.gameObject)
                    continue;

                var source = new SceneGraphSource
                {
                    objectPath = HierarchyPath(button.transform),
                    kind = SceneGraphSourceKinds.MenuButton,
                    displayName = button.gameObject.name,
                    windowId = id,
                    isJump = isJump
                };

                if (!string.IsNullOrEmpty(id) && database != null)
                {
                    string target = isJump ? database.GetJumpSceneName(id) : database.GetAsyncSceneName(id);
                    source.mode = isJump ? SceneGraphModes.Jump : SceneGraphModes.Async;
                    source.targetSceneName = target;
                    source.targetScenePath = ResolveScenePath(target);
                    source.resolved = !string.IsNullOrEmpty(source.targetScenePath);
                }
                else if (eventTarget is NovelEntryPoint otherEntry)
                {
                    var entrySo = new SerializedObject(otherEntry);
                    string runtimeScene = ReadString(entrySo, "runtimeScene");
                    source.mode = SceneGraphModes.Direct;
                    source.runtimeScene = runtimeScene;
                    source.targetSceneName = runtimeScene;
                    source.targetScenePath = ResolveScenePath(runtimeScene);
                    source.resolved = !string.IsNullOrEmpty(source.targetScenePath);
                    source.eventTarget = otherEntry.name + "." + eventMethod;
                }
                else if (eventTarget != null || !string.IsNullOrEmpty(eventMethod))
                {
                    source.mode = SceneGraphModes.Event;
                    source.eventTarget = (eventTarget != null ? eventTarget.name : "?") + "." + eventMethod;
                }

                graph.sources.Add(source);
            }

            foreach (var entry in behaviours.OfType<NovelEntryPoint>())
            {
                var so = new SerializedObject(entry);
                string runtimeScene = ReadString(so, "runtimeScene");
                graph.sources.Add(new SceneGraphSource
                {
                    objectPath = HierarchyPath(entry.transform),
                    kind = SceneGraphSourceKinds.NovelEntry,
                    displayName = entry.gameObject.name,
                    runtimeScene = runtimeScene,
                    mode = string.IsNullOrEmpty(runtimeScene) ? SceneGraphModes.None : SceneGraphModes.Direct,
                    targetSceneName = runtimeScene,
                    targetScenePath = ResolveScenePath(runtimeScene),
                    resolved = !string.IsNullOrEmpty(ResolveScenePath(runtimeScene))
                });
            }

            // 过场动画出口：脚本只持有流程 ID，目标场景由本场景 WindowDatabase 的跳转列表解析。
            foreach (var transition in behaviours.OfType<AnimSceneTransition>())
            {
                var so = new SerializedObject(transition);
                string flowId = ReadString(so, "流程ID");
                bool useManager = ReadBool(so, "交给流程管理器");

                var source = new SceneGraphSource
                {
                    objectPath = HierarchyPath(transition.transform),
                    kind = SceneGraphSourceKinds.SceneFlow,
                    displayName = "开场动画",
                    windowId = flowId,
                    isJump = true,
                    mode = SceneGraphModes.None
                };

                if (!string.IsNullOrEmpty(flowId))
                {
                    string target = useManager && database != null ? database.GetJumpSceneName(flowId) : null;
                    source.mode = SceneGraphModes.Jump;
                    source.targetSceneName = target;
                    source.targetScenePath = ResolveScenePath(target);
                    source.resolved = !string.IsNullOrEmpty(source.targetScenePath);
                }

                graph.sources.Add(source);
            }

            foreach (var button in behaviours.OfType<Button>())
            {
                if (button.GetComponent<UIMenuButton>() != null) continue;

                var so = new SerializedObject(button);
                var calls = so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
                if (calls == null || calls.arraySize == 0) continue;

                UnityEngine.Object target = null;
                string method = null;
                for (int i = 0; i < calls.arraySize; i++)
                {
                    var call = calls.GetArrayElementAtIndex(i);
                    var targetRef = call.FindPropertyRelative("m_Target").objectReferenceValue;
                    if (targetRef == null) continue;
                    target = targetRef;
                    method = call.FindPropertyRelative("m_MethodName").stringValue;
                    break;
                }
                if (target == null && string.IsNullOrEmpty(method)) continue;

                var source = new SceneGraphSource
                {
                    objectPath = HierarchyPath(button.transform),
                    kind = SceneGraphSourceKinds.ButtonEvent,
                    displayName = button.gameObject.name,
                    eventTarget = (target != null ? target.name + "." : "?.") + method,
                    mode = SceneGraphModes.Event
                };

                string targetTypeName = target != null ? target.GetType().Name : null;
                if (targetTypeName == "NovelPresentationController" && method == "ReturnToLobby")
                {
                    source.mode = SceneGraphModes.Direct;
                    source.targetSceneName = "Lobby";
                    source.targetScenePath = ResolveScenePath("Lobby");
                    source.resolved = !string.IsNullOrEmpty(source.targetScenePath);
                }
                else if (target is NovelEntryPoint entry && (method == "StartNewGame" || method == "ContinueGame"))
                {
                    var entrySo = new SerializedObject(entry);
                    string runtimeScene = ReadString(entrySo, "runtimeScene");
                    source.mode = SceneGraphModes.Direct;
                    source.targetSceneName = runtimeScene;
                    source.targetScenePath = ResolveScenePath(runtimeScene);
                    source.resolved = !string.IsNullOrEmpty(source.targetScenePath);
                }

                graph.sources.Add(source);
            }

            foreach (var source in graph.sources)
                if (source.mode == SceneGraphModes.None && string.IsNullOrEmpty(source.windowId) &&
                    string.IsNullOrEmpty(source.runtimeScene) && string.IsNullOrEmpty(source.eventTarget))
                    source.mode = SceneGraphModes.None;

            // 流程出口已经按跳转表解析出目标，不再重复画一条“场景级连接”。
            var flowIds = graph.sources
                .Where(source => source.kind == SceneGraphSourceKinds.SceneFlow && !string.IsNullOrEmpty(source.windowId))
                .Select(source => source.windowId)
                .ToList();
            if (flowIds.Count > 0)
                graph.windowDefs.RemoveAll(def => def.isJump && flowIds.Contains(def.id));

            return graph;
        }

        private static SceneGraphWindowDef MakeDef(WindowDatabase database, string id, string sceneName, bool isJump)
        {
            return new SceneGraphWindowDef
            {
                objectPath = HierarchyPath(database.transform),
                id = id,
                sceneName = sceneName,
                isJump = isJump,
                targetScenePath = ResolveScenePath(sceneName),
                resolved = !string.IsNullOrEmpty(ResolveScenePath(sceneName))
            };
        }

        public static string ResolveScenePath(string sceneNameOrPath)
        {
            if (string.IsNullOrWhiteSpace(sceneNameOrPath)) return null;

            string value = sceneNameOrPath.Trim();
            if (value.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                string normalized = value.Replace('\\', '/');
                if (!normalized.StartsWith("Assets/", StringComparison.Ordinal)) normalized = "Assets/" + normalized;
                return File.Exists(Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? string.Empty, normalized))
                    ? normalized
                    : null;
            }

            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (string.Equals(Path.GetFileNameWithoutExtension(scene.path), value, StringComparison.OrdinalIgnoreCase))
                    return scene.path;
            }

            var matches = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => string.Equals(Path.GetFileNameWithoutExtension(path), value, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return matches.Count == 1 ? matches[0] : null;
        }

        public static bool IsInBuild(string scenePath)
        {
            return EditorBuildSettings.scenes.Any(scene =>
                scene.enabled && string.Equals(scene.path, scenePath, StringComparison.OrdinalIgnoreCase));
        }

        private static string ReadString(SerializedObject so, string name)
        {
            var property = so.FindProperty(name);
            return property != null ? property.stringValue : null;
        }

        private static bool ReadBool(SerializedObject so, string name)
        {
            var property = so.FindProperty(name);
            return property != null && property.boolValue;
        }

        public static string HierarchyPath(Transform transform)
        {
            var parts = new List<string>();
            var current = transform;
            while (current != null)
            {
                parts.Insert(0, current.name);
                current = current.parent;
            }
            return string.Join("/", parts);
        }

        public static string RoleFor(string sceneName)
        {
            string value = (sceneName ?? string.Empty).ToLowerInvariant();
            if (value.Contains("login") || sceneName.Contains("登录")) return "login";
            if (value.Contains("lobby") || value.Contains("hall") || sceneName.Contains("大厅")) return "lobby";
            if (value.Contains("setting") || sceneName.Contains("设置")) return "settings";
            if (value.Contains("novel") || value.Contains("story") || sceneName.Contains("剧情")) return "novel";
            if (value.Contains("game") || value.Contains("level") || value.Contains("play") || sceneName.Contains("游戏")) return "gameplay";
            return "other";
        }
    }
}
