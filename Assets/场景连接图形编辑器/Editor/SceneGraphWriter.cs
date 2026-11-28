using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Lament.SceneGraph
{
    /// <summary>
    /// Writes graph edits back into the scene components. Only the source scene is
    /// touched: the button/entry lives there, and the WindowDatabase it consults is
    /// expected to be in the same scene (SettingsWindowManager falls back to
    /// FindAnyObjectByType).
    /// </summary>
    public static class SceneGraphWriter
    {
        public static bool Connect(SceneGraphSource source, string sourceScenePath, string targetScenePath,
            bool isJump, out string message)
        {
            message = null;
            if (source == null || string.IsNullOrEmpty(sourceScenePath) || string.IsNullOrEmpty(targetScenePath))
            {
                message = "缺少源或目标场景";
                return false;
            }

            if (source.kind == SceneGraphSourceKinds.ButtonEvent)
            {
                message = "Button.onClick 事件只读，无法自动写回；请使用 UIMenuButton 或 NovelEntryPoint";
                return false;
            }

            string targetSceneName = Path.GetFileNameWithoutExtension(targetScenePath);
            var session = new SceneEditSession(sourceScenePath);
            try
            {
                if (!session.Open(out message)) return false;
                var scene = session.Scene;
                var gameObject = FindByPath(scene, source.objectPath);
                if (gameObject == null)
                {
                    message = $"找不到对象：{source.objectPath}";
                    return false;
                }

                if (source.kind == SceneGraphSourceKinds.MenuButton)
                {
                    var button = gameObject.GetComponent<UIMenuButton>();
                    if (button == null)
                    {
                        message = $"{source.objectPath} 上没有 UIMenuButton";
                        return false;
                    }

                    var database = FindDatabase(scene);
                    if (database == null)
                    {
                        message = "源场景中没有 WindowDatabase，无法登记窗口 ID";
                        return false;
                    }

                    string id = string.IsNullOrEmpty(source.windowId)
                        ? GenerateId(scene, source.displayName, isJump)
                        : source.windowId;

                    Undo.RecordObject(database, "Scene Graph: window config");
                    var list = isJump ? database.jumpConfigs : database.configs;
                    var existing = list.FirstOrDefault(config => config != null && config.id == id);
                    if (existing != null) existing.sceneName = targetSceneName;
                    else list.Add(new WindowDatabase.WindowConfig { id = id, sceneName = targetSceneName });
                    EditorUtility.SetDirty(database);

                    Undo.RecordObject(button, "Scene Graph: menu button target");
                    var serialized = new SerializedObject(button);
                    var idProperty = serialized.FindProperty("打开窗口ID");
                    var jumpProperty = serialized.FindProperty("窗口是否为跳转");
                    if (idProperty == null || jumpProperty == null)
                    {
                        message = "UIMenuButton 的打开窗口ID/窗口是否为跳转字段不存在";
                        return false;
                    }
                    idProperty.stringValue = id;
                    jumpProperty.boolValue = isJump;
                    serialized.ApplyModifiedProperties();
                    EditorUtility.SetDirty(button);

                    message = $"已写回：{id} → {targetSceneName}（{(isJump ? "跳转" : "异步")}）";
                }
                else if (source.kind == SceneGraphSourceKinds.SceneFlow)
                {
                    var transition = gameObject.GetComponent<AnimSceneTransition>();
                    if (transition == null)
                    {
                        message = $"{source.objectPath} 上没有 AnimSceneTransition";
                        return false;
                    }

                    var database = FindDatabase(scene);
                    if (database == null)
                    {
                        message = "源场景中没有 WindowDatabase，无法登记流程 ID";
                        return false;
                    }

                    string id = string.IsNullOrEmpty(source.windowId)
                        ? GenerateId(scene, source.displayName, true)
                        : source.windowId;

                    Undo.RecordObject(database, "Scene Graph: flow config");
                    var flow = database.jumpConfigs.FirstOrDefault(config => config != null && config.id == id);
                    if (flow != null) flow.sceneName = targetSceneName;
                    else database.jumpConfigs.Add(new WindowDatabase.WindowConfig { id = id, sceneName = targetSceneName });
                    EditorUtility.SetDirty(database);

                    Undo.RecordObject(transition, "Scene Graph: scene flow target");
                    var serialized = new SerializedObject(transition);
                    var idProperty = serialized.FindProperty("流程ID");
                    var managerProperty = serialized.FindProperty("交给流程管理器");
                    if (idProperty == null)
                    {
                        message = "AnimSceneTransition.流程ID 字段不存在";
                        return false;
                    }
                    idProperty.stringValue = id;
                    if (managerProperty != null) managerProperty.boolValue = true;
                    serialized.ApplyModifiedProperties();
                    EditorUtility.SetDirty(transition);

                    message = $"已写回：流程 {id} → {targetSceneName}";
                }
                else if (source.kind == SceneGraphSourceKinds.NovelEntry)
                {
                    var entry = gameObject.GetComponent<NovelEntryPoint>();
                    if (entry == null)
                    {
                        message = $"{source.objectPath} 上没有 NovelEntryPoint";
                        return false;
                    }

                    Undo.RecordObject(entry, "Scene Graph: novel entry target");
                    var serialized = new SerializedObject(entry);
                    var runtimeProperty = serialized.FindProperty("runtimeScene");
                    if (runtimeProperty == null)
                    {
                        message = "NovelEntryPoint.runtimeScene 字段不存在";
                        return false;
                    }
                    runtimeProperty.stringValue = targetSceneName;
                    serialized.ApplyModifiedProperties();
                    EditorUtility.SetDirty(entry);

                    message = $"已写回：NovelEntryPoint → {targetSceneName}";
                }
                else
                {
                    message = "未知的源类型：" + source.kind;
                    return false;
                }

                session.MarkDirty();
                session.Commit();
                return true;
            }
            catch (Exception ex)
            {
                message = "写回失败：" + ex.Message;
                return false;
            }
            finally
            {
                session.Dispose();
            }
        }

        public static bool ConnectWindowDef(SceneGraphWindowDef def, string sourceScenePath,
            string targetScenePath, out string message)
        {
            message = null;
            if (def == null || string.IsNullOrEmpty(sourceScenePath) || string.IsNullOrEmpty(targetScenePath))
            {
                message = "缺少源或目标场景";
                return false;
            }

            string targetSceneName = Path.GetFileNameWithoutExtension(targetScenePath);
            var session = new SceneEditSession(sourceScenePath);
            try
            {
                if (!session.Open(out message)) return false;
                var scene = session.Scene;
                var database = FindDatabase(scene);
                if (database == null)
                {
                    message = "源场景中没有 WindowDatabase";
                    return false;
                }

                Undo.RecordObject(database, "Scene Graph: update window config");
                var list = def.isJump ? database.jumpConfigs : database.configs;
                var config = list.FirstOrDefault(item => item != null && item.id == def.id);
                if (config == null)
                {
                    message = $"数据库中没有 ID：{def.id}";
                    return false;
                }

                config.sceneName = targetSceneName;
                EditorUtility.SetDirty(database);
                session.MarkDirty();
                session.Commit();

                message = $"已写回：{def.id} → {targetSceneName}";
                return true;
            }
            catch (Exception ex)
            {
                message = "写回失败：" + ex.Message;
                return false;
            }
            finally
            {
                session.Dispose();
            }
        }

        public static bool Disconnect(SceneGraphSource source, string sourceScenePath, out string message,
            bool alsoClearEvent = false)
        {
            message = null;
            if (source == null || string.IsNullOrEmpty(sourceScenePath))
            {
                message = "缺少源场景";
                return false;
            }

            var session = new SceneEditSession(sourceScenePath);
            try
            {
                if (!session.Open(out message)) return false;
                var gameObject = FindByPath(session.Scene, source.objectPath);
                if (gameObject == null)
                {
                    message = "找不到对象：" + source.objectPath;
                    return false;
                }

                if (source.kind == SceneGraphSourceKinds.MenuButton)
                {
                    var button = gameObject.GetComponent<UIMenuButton>();
                    if (button == null)
                    {
                        message = "对象上没有 UIMenuButton";
                        return false;
                    }

                    Undo.RecordObject(button, "Scene Graph: clear menu button");
                    var serialized = new SerializedObject(button);
                    var idProperty = serialized.FindProperty("打开窗口ID");
                    var jumpProperty = serialized.FindProperty("窗口是否为跳转");
                    if (idProperty == null || jumpProperty == null)
                    {
                        message = "字段不存在";
                        return false;
                    }
                    idProperty.stringValue = string.Empty;
                    jumpProperty.boolValue = false;
                    serialized.ApplyModifiedProperties();
                    EditorUtility.SetDirty(button);
                    message = "已清除按钮连接（数据库条目保留，可由其他按钮复用）";
                    message += alsoClearEvent
                        ? ClearMatchingEventCall(button, source.eventTarget)
                        : DescribeResidualEvent(button);
                }
                else if (source.kind == SceneGraphSourceKinds.SceneFlow)
                {
                    var transition = gameObject.GetComponent<AnimSceneTransition>();
                    if (transition == null)
                    {
                        message = "对象上没有 AnimSceneTransition";
                        return false;
                    }

                    Undo.RecordObject(transition, "Scene Graph: clear scene flow");
                    var serialized = new SerializedObject(transition);
                    var idProperty = serialized.FindProperty("流程ID");
                    if (idProperty == null)
                    {
                        message = "流程ID 字段不存在";
                        return false;
                    }
                    idProperty.stringValue = string.Empty;
                    serialized.ApplyModifiedProperties();
                    EditorUtility.SetDirty(transition);
                    message = "已清除流程出口（数据库条目保留，可由其它出口复用）";
                }
                else if (source.kind == SceneGraphSourceKinds.NovelEntry)
                {
                    var entry = gameObject.GetComponent<NovelEntryPoint>();
                    if (entry == null)
                    {
                        message = "对象上没有 NovelEntryPoint";
                        return false;
                    }

                    Undo.RecordObject(entry, "Scene Graph: clear novel entry");
                    var serialized = new SerializedObject(entry);
                    var runtimeProperty = serialized.FindProperty("runtimeScene");
                    if (runtimeProperty == null)
                    {
                        message = "runtimeScene 字段不存在";
                        return false;
                    }
                    runtimeProperty.stringValue = string.Empty;
                    serialized.ApplyModifiedProperties();
                    EditorUtility.SetDirty(entry);
                    message = "已清除入口目标";
                }
                else if (source.kind == SceneGraphSourceKinds.ButtonEvent)
                {
                    var button = gameObject.GetComponent<UnityEngine.UI.Button>();
                    if (button == null)
                    {
                        message = "对象上没有 Button";
                        return false;
                    }

                    string removed = RemoveOnClickCall(button, source.eventTarget);
                    if (string.IsNullOrEmpty(removed))
                    {
                        message = "未在 Button.onClick 里找到匹配的调用（可能已被改过）";
                        return false;
                    }

                    message = $"已移除 Button.onClick 里的 {removed}";
                }
                else
                {
                    message = "未知的源类型：" + source.kind;
                    return false;
                }

                session.MarkDirty();
                session.Commit();
                return true;
            }
            catch (Exception ex)
            {
                message = "清除失败：" + ex.Message;
                return false;
            }
            finally
            {
                session.Dispose();
            }
        }

        /// <summary>按 targetName.method 精确移除 Button.onClick 里的一次调用，返回被移除的签名。</summary>
        private static string RemoveOnClickCall(UnityEngine.UI.Button button, string eventTarget)
        {
            if (button == null || string.IsNullOrEmpty(eventTarget)) return null;

            int dot = eventTarget.LastIndexOf('.');
            if (dot <= 0) return null;

            string targetName = eventTarget.Substring(0, dot);
            string method = eventTarget.Substring(dot + 1);

            var serialized = new SerializedObject(button);
            var calls = serialized.FindProperty("m_OnClick")?.FindPropertyRelative("m_PersistentCalls.m_Calls");
            if (calls == null) return null;

            for (int i = 0; i < calls.arraySize; i++)
            {
                var call = calls.GetArrayElementAtIndex(i);
                var target = call.FindPropertyRelative("m_Target").objectReferenceValue;
                if (target == null || target.name != targetName) continue;
                if (call.FindPropertyRelative("m_MethodName").stringValue != method) continue;

                Undo.RecordObject(button, "Scene Graph: remove onClick call");
                calls.DeleteArrayElementAtIndex(i);
                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(button);
                return targetName + "." + method;
            }

            return null;
        }

        /// <summary>
        /// 删除一条“场景级连接”（WindowDatabase 条目）。没有按钮/流程再引用该 id 时直接删条目，
        /// 仍有引用时只清空目标场景，避免把别处的按钮一起弄坏。
        /// </summary>
        public static bool DisconnectWindowDef(SceneGraphWindowDef def, string sourceScenePath, out string message)
        {
            message = null;
            if (def == null || string.IsNullOrEmpty(sourceScenePath) || string.IsNullOrEmpty(def.id))
            {
                message = "缺少连接信息";
                return false;
            }

            var session = new SceneEditSession(sourceScenePath);
            try
            {
                if (!session.Open(out message)) return false;

                var database = FindDatabase(session.Scene);
                if (database == null)
                {
                    message = "源场景中没有 WindowDatabase";
                    return false;
                }

                var list = def.isJump ? database.jumpConfigs : database.configs;
                var config = list.FirstOrDefault(item => item != null && item.id == def.id);
                if (config == null)
                {
                    message = $"数据库里已经没有 ID：{def.id}";
                    return false;
                }

                int references = CountIdReferences(session.Scene, def.id, def.isJump);
                Undo.RecordObject(database, "Scene Graph: delete window config");
                if (references > 0)
                {
                    config.sceneName = string.Empty;
                    message = $"已清空 {def.id} 的目标场景（仍有 {references} 个对象引用该 id）";
                }
                else
                {
                    list.Remove(config);
                    message = $"已从 WindowDatabase 移除 {def.id}";
                }

                EditorUtility.SetDirty(database);
                session.MarkDirty();
                session.Commit();
                return true;
            }
            catch (Exception ex)
            {
                message = "删除失败：" + ex.Message;
                return false;
            }
            finally
            {
                session.Dispose();
            }
        }

        private static int CountIdReferences(Scene scene, string id, bool isJump)
        {
            int count = 0;

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var button in root.GetComponentsInChildren<UIMenuButton>(true))
                {
                    var serialized = new SerializedObject(button);
                    string buttonId = serialized.FindProperty("打开窗口ID")?.stringValue;
                    bool buttonJump = serialized.FindProperty("窗口是否为跳转")?.boolValue ?? false;
                    if (buttonId == id && buttonJump == isJump) count++;
                }

                foreach (var transition in root.GetComponentsInChildren<AnimSceneTransition>(true))
                {
                    var serialized = new SerializedObject(transition);
                    string flowId = serialized.FindProperty("流程ID")?.stringValue;
                    if (flowId == id && isJump) count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 「点击结束」还直接调着别的对象时提醒一句：清掉窗口 ID 后，那条连线其实来自这个事件，
        /// 不处理事件的话图上仍然会有一条连线，看起来像"删了没生效"。
        /// </summary>
        private static string DescribeResidualEvent(UIMenuButton button)
        {
            if (button == null) return string.Empty;

            var serialized = new SerializedObject(button);
            var calls = serialized.FindProperty("点击结束")?.FindPropertyRelative("m_PersistentCalls.m_Calls");
            if (calls == null || calls.arraySize == 0) return string.Empty;

            for (int i = 0; i < calls.arraySize; i++)
            {
                var call = calls.GetArrayElementAtIndex(i);
                var target = call.FindPropertyRelative("m_Target").objectReferenceValue;
                if (target == null) continue;

                string method = call.FindPropertyRelative("m_MethodName").stringValue;
                return $"；注意：「点击结束」仍在调用 {target.name}.{method}，图上这条线来自该事件，需改事件才会真正断开";
            }

            return string.Empty;
        }

        /// <summary>
        /// 把「点击结束」里产生这条连线的那一次调用移除（targetName.method 精确匹配），
        /// 用于"删掉这条连接"要连事件一起断的情况。
        /// </summary>
        private static string ClearMatchingEventCall(UIMenuButton button, string eventTarget)
        {
            if (button == null || string.IsNullOrEmpty(eventTarget)) return DescribeResidualEvent(button);

            int dot = eventTarget.LastIndexOf('.');
            if (dot <= 0) return DescribeResidualEvent(button);

            string targetName = eventTarget.Substring(0, dot);
            string method = eventTarget.Substring(dot + 1);

            var serialized = new SerializedObject(button);
            var calls = serialized.FindProperty("点击结束")?.FindPropertyRelative("m_PersistentCalls.m_Calls");
            if (calls == null) return string.Empty;

            for (int i = 0; i < calls.arraySize; i++)
            {
                var call = calls.GetArrayElementAtIndex(i);
                var target = call.FindPropertyRelative("m_Target").objectReferenceValue;
                if (target == null || target.name != targetName) continue;
                if (call.FindPropertyRelative("m_MethodName").stringValue != method) continue;

                Undo.RecordObject(button, "Scene Graph: remove click event");
                calls.DeleteArrayElementAtIndex(i);
                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(button);
                return $"；同时移除了「点击结束」里的 {targetName}.{method} 调用";
            }

            return DescribeResidualEvent(button);
        }

        private static WindowDatabase FindDatabase(Scene scene)        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var database = root.GetComponentInChildren<WindowDatabase>(true);
                if (database != null) return database;
            }
            return null;
        }

        private static GameObject FindByPath(Scene scene, string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (SceneGraphScanner.HierarchyPath(transform) == path)
                        return transform.gameObject;
                }
            }
            return null;
        }

        private static string GenerateId(Scene scene, string displayName, bool isJump)
        {
            var database = FindDatabase(scene);
            string sceneName = Path.GetFileNameWithoutExtension(scene.path);
            string baseId = Sanitize((sceneName + "_" + displayName).ToLowerInvariant());
            if (string.IsNullOrEmpty(baseId)) baseId = "window";

            string id = baseId;
            int suffix = 2;
            while (database != null &&
                   (isJump ? database.jumpConfigs : database.configs).Any(config => config != null && config.id == id))
            {
                id = baseId + "_" + suffix;
                suffix++;
            }
            return id;
        }

        private static string Sanitize(string value)
        {
            var chars = value.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_').ToArray();
            return new string(chars).Trim('_');
        }
    }

    /// <summary>
    /// Opens a scene additively when needed and commits it. A scene the user already has
    /// open is left dirty (not force-saved); a scene this session opened is saved and closed.
    /// </summary>
    public sealed class SceneEditSession : IDisposable
    {
        public Scene Scene { get; private set; }
        private readonly string _scenePath;
        private bool _owned;
        private bool _dirty;

        public SceneEditSession(string scenePath)
        {
            _scenePath = scenePath;
        }

        public bool Open(out string error)
        {
            error = null;
            var existing = SceneManager.GetSceneByPath(_scenePath);
            if (existing.IsValid() && existing.isLoaded)
            {
                Scene = existing;
                return true;
            }

            if (!File.Exists(Path.Combine(
                    Path.GetDirectoryName(Application.dataPath) ?? string.Empty,
                    _scenePath.Replace('/', Path.DirectorySeparatorChar))))
            {
                error = "场景文件不存在：" + _scenePath;
                return false;
            }

            Scene = EditorSceneManager.OpenScene(_scenePath, OpenSceneMode.Additive);
            _owned = true;
            return true;
        }

        public void MarkDirty()
        {
            _dirty = true;
            EditorSceneManager.MarkSceneDirty(Scene);
        }

        public void Commit()
        {
            if (!_owned || !_dirty) return;
            EditorSceneManager.SaveScene(Scene);
            _dirty = false;
        }

        public void Dispose()
        {
            if (_owned && Scene.IsValid() && Scene.isLoaded)
            {
                if (_dirty) EditorSceneManager.SaveScene(Scene);
                EditorSceneManager.CloseScene(Scene, true);
            }
        }
    }
}
