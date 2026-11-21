using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Codely.UnityMcp
{
    /// <summary>
    /// Editor lifecycle and state. Long transitions (play/stop/refresh/compile) use the
    /// start-then-poll contract because Unity may reload the domain mid-operation and
    /// drop the socket.
    /// </summary>
    public static class ManageEditorTool
    {
        public static ToolResult Handle(IDictionary<string, object> p)
        {
            string action = (JsonUtil.Str(p, "action", "get_state") ?? "get_state").Trim().ToLowerInvariant();

            switch (action)
            {
                case "get_state":
                case "get_current_state":
                    return new ToolResult("Editor state collected.", State());

                case "play": return StartPlay();
                case "stop": return StartStop();
                case "pause": return SetPaused(p);
                case "resume": return Resume(p);
                case "step": return Step();
                case "get_operation": return new ToolResult("Operation status.", EditorOperation.Status());

                case "refresh": return StartRefresh(p);
                case "request_compile":
                case "start_compilation_pipeline":
                    return StartCompile(p);

                case "get_compilation_summary": return new ToolResult("Compilation summary.", CompilationSummary());
                case "wait_for_idle": return new ToolResult("Idle check.", IdleState());

                case "get_tags": return new ToolResult("Tags listed.", GetTags());
                case "ensure_tag":
                case "add_tag":
                    return AddTag(p, action == "ensure_tag");
                case "remove_tag": return RemoveTag(p);

                case "get_layers": return new ToolResult("Layers listed.", GetLayers());
                case "ensure_layer":
                case "add_layer":
                    return AddLayer(p, action == "ensure_layer");
                case "remove_layer": return RemoveLayer(p);

                case "get_windows": return new ToolResult("Windows listed.", GetWindows());
                case "focus_window": return FocusWindow(p);

                case "get_selection": return new ToolResult("Selection read.", GetSelection());
                case "get_active_tool":
                    return new ToolResult("Active tool read.", new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["tool"] = Tools.current.ToString()
                    });
                case "set_active_tool": return SetActiveTool(p);

                case "get_project_root":
                    return new ToolResult("Project root resolved.", new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["project_root"] = McpBridgeServer.ProjectRoot,
                        ["data_path"] = Application.dataPath
                    });

                case "drain_agent_input":
                    return new ToolResult("No pending editor-to-agent input in this bridge.", new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["messages"] = new List<object>()
                    });

                default:
                    throw new ToolException("unknown_action",
                        $"Unknown manage_editor action '{action}'. Known actions: get_state, play, stop, pause, resume, step, " +
                        "get_operation, refresh, request_compile, get_compilation_summary, wait_for_idle, get_tags, ensure_tag, " +
                        "add_tag, remove_tag, get_layers, ensure_layer, add_layer, remove_layer, get_windows, focus_window, " +
                        "get_selection, get_active_tool, set_active_tool, get_project_root.");
            }
        }

        // ---- state ---------------------------------------------------------- //

        private static Dictionary<string, object> State()
        {
            var scene = SceneManager.GetActiveScene();
            var selection = GetSelection();

            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["isPlaying"] = EditorApplication.isPlaying,
                ["isPaused"] = EditorApplication.isPaused,
                ["isCompiling"] = EditorApplication.isCompiling,
                ["isUpdating"] = EditorApplication.isUpdating,
                ["isPlayingOrWillChangePlaymode"] = EditorApplication.isPlayingOrWillChangePlaymode,
                ["playMode"] = EditorApplication.isPlaying
                    ? (EditorApplication.isPaused ? "paused" : "playing")
                    : (EditorApplication.isPlayingOrWillChangePlaymode ? "transitioning" : "stopped"),
                ["activeScene"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["name"] = scene.name,
                    ["path"] = scene.path,
                    ["isDirty"] = scene.isDirty
                },
                ["loadedSceneCount"] = SceneManager.sceneCount,
                ["selection"] = selection,
                ["activeTool"] = Tools.current.ToString(),
                ["unityVersion"] = Application.unityVersion,
                ["projectRoot"] = McpBridgeServer.ProjectRoot,
                ["operation"] = EditorOperation.Status(),
                ["console"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["error_count"] = ConsoleBuffer.Read(new Dictionary<string, object>(StringComparer.Ordinal)
                    { ["types"] = new List<object> { "error" }, ["count"] = 1 })["error_count"],
                    ["warning_count"] = ConsoleBuffer.Read(new Dictionary<string, object>(StringComparer.Ordinal)
                    { ["types"] = new List<object> { "warning" }, ["count"] = 1 })["warning_count"]
                }
            };
        }

        private static Dictionary<string, object> GetSelection()
        {
            var objects = new List<object>();
            foreach (var obj in Selection.objects)
            {
                if (obj == null) continue;
                string assetPath = AssetDatabase.GetAssetPath(obj);
                objects.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["name"] = obj.name,
                    ["type"] = obj.GetType().Name,
                    ["instanceID"] = UnityEngine.EntityId.ToULong(obj.GetEntityId()),
                    ["assetPath"] = string.IsNullOrEmpty(assetPath) ? null : assetPath
                });
            }

            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["count"] = objects.Count,
                ["activeObject"] = Selection.activeObject != null ? Selection.activeObject.name : null,
                ["activeInstanceID"] = Selection.activeObject != null
                    ? UnityEngine.EntityId.ToULong(Selection.activeObject.GetEntityId())
                    : (object)null,
                ["objects"] = objects,
                ["assetGUIDs"] = new List<object>(Selection.assetGUIDs)
            };
        }

        // ---- play/stop ------------------------------------------------------ //

        private static ToolResult StartPlay()
        {
            if (EditorApplication.isPlaying)
                return new ToolResult("Already in Play Mode.", new Dictionary<string, object>(StringComparer.Ordinal)
                { ["status"] = "done", ["is_playing"] = true });

            if (EditorApplication.isCompiling)
                throw new ToolException("compiling", "Cannot enter Play Mode while scripts are compiling. Poll manage_editor.get_operation or wait_for_idle.");

            EditorOperation.Begin("play");
            EditorApplication.delayCall += () => EditorApplication.isPlaying = true;

            return new ToolResult("Play Mode start requested.", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["status"] = "started",
                ["operation"] = "play",
                ["next"] = "Poll manage_editor.get_operation until state=done (it covers the domain reload)."
            });
        }

        private static ToolResult StartStop()
        {
            if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
                return new ToolResult("Already stopped.", new Dictionary<string, object>(StringComparer.Ordinal)
                { ["status"] = "done", ["is_playing"] = false });

            EditorOperation.Begin("stop");
            EditorApplication.delayCall += () => EditorApplication.isPlaying = false;

            return new ToolResult("Play Mode stop requested.", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["status"] = "started",
                ["operation"] = "stop",
                ["warning"] = "Stopping Play Mode discards all runtime state.",
                ["next"] = "Poll manage_editor.get_operation until state=done."
            });
        }

        private static ToolResult SetPaused(IDictionary<string, object> p)
        {
            if (!EditorApplication.isPlaying)
                throw new ToolException("not_playing", "Pause requires Play Mode. Call manage_editor.play first.");

            bool desired = JsonUtil.Bool(p, "targetPaused", !EditorApplication.isPaused);
            EditorApplication.isPaused = desired;
            return new ToolResult(desired ? "Paused." : "Unpaused.", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["isPaused"] = EditorApplication.isPaused
            });
        }

        private static ToolResult Resume(IDictionary<string, object> p)
        {
            if (!EditorApplication.isPlaying)
                throw new ToolException("not_playing", "Resume requires Play Mode. Call manage_editor.play first.");

            bool singleFrame = JsonUtil.Bool(p, "singleFrame", false);
            if (singleFrame) EditorApplication.Step();
            else EditorApplication.isPaused = false;

            return new ToolResult(singleFrame ? "Advanced one frame." : "Resumed.", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["isPaused"] = EditorApplication.isPaused,
                ["singleFrame"] = singleFrame
            });
        }

        private static ToolResult Step()
        {
            if (!EditorApplication.isPlaying)
                throw new ToolException("not_playing", "Step requires Play Mode. Call manage_editor.play first.");
            EditorApplication.Step();
            return new ToolResult("Advanced one frame.", new Dictionary<string, object>(StringComparer.Ordinal)
            { ["singleFrame"] = true });
        }

        // ---- refresh/compile ------------------------------------------------- //

        private static ToolResult StartRefresh(IDictionary<string, object> p)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new ToolException("blocked_in_play_mode", "Refresh is blocked in Play Mode. Call manage_editor.stop first.");

            string mode = NormalizeConsoleMode(p);
            EditorOperation.Begin("refresh");
            EditorApplication.delayCall += () =>
            {
                try { AssetDatabase.Refresh(ImportAssetOptions.Default); }
                catch (Exception ex) { Debug.LogError("Codely Unity MCP refresh failed: " + ex.Message); }
            };

            return new ToolResult("Asset refresh requested.", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["status"] = "started",
                ["operation"] = "refresh",
                ["console_mode"] = mode,
                ["next"] = "Poll manage_editor.get_operation until state=done, then read_console if you expect diagnostics."
            });
        }

        private static ToolResult StartCompile(IDictionary<string, object> p)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new ToolException("blocked_in_play_mode", "Compilation is blocked in Play Mode. Call manage_editor.stop first.");

            bool clear = JsonUtil.Bool(p, "clearCache", false);
            string mode = NormalizeConsoleMode(p);
            EditorOperation.Begin("compile");

            EditorApplication.delayCall += () =>
            {
                try
                {
                    if (clear)
                        CompilationPipeline.RequestScriptCompilation(UnityEditor.Compilation.RequestScriptCompilationOptions.CleanBuildCache);
                    else
                        CompilationPipeline.RequestScriptCompilation();
                }
                catch (Exception ex)
                {
                    Debug.LogError("Codely Unity MCP compile request failed: " + ex.Message);
                }
            };

            return new ToolResult("Script compilation requested.", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["status"] = "started",
                ["operation"] = "compile",
                ["clear_cache"] = clear,
                ["console_mode"] = mode,
                ["next"] = "Poll manage_editor.get_operation until state=done; the domain may reload, reconnect and continue polling."
            });
        }

        private static string NormalizeConsoleMode(IDictionary<string, object> p)
        {
            string mode = JsonUtil.Str(p, "console_mode");
            if (string.IsNullOrEmpty(mode))
                mode = JsonUtil.Has(p, "include_console")
                    ? (JsonUtil.Bool(p, "include_console", true) ? "full" : "none")
                    : "errors";
            mode = mode.ToLowerInvariant();
            if (mode != "none" && mode != "errors" && mode != "full")
                throw new ToolException("invalid_console_mode", "console_mode must be none, errors or full.");
            return mode;
        }

        private static Dictionary<string, object> CompilationSummary()
        {
            var all = ConsoleBuffer.Read(new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["types"] = new List<object> { "error", "warning" },
                ["count"] = 20,
                ["format"] = "plain",
                ["collapse"] = true
            });

            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["isCompiling"] = EditorApplication.isCompiling,
                ["isUpdating"] = EditorApplication.isUpdating,
                ["error_count"] = all["error_count"],
                ["warning_count"] = all["warning_count"],
                ["recent_errors_and_warnings"] = all["entries"],
                ["operation"] = EditorOperation.Status(),
                ["note"] = "Counts cover everything captured since the console buffer was last cleared."
            };
        }

        private static Dictionary<string, object> IdleState()
        {
            bool idle = !EditorApplication.isCompiling && !EditorApplication.isUpdating;
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["idle"] = idle,
                ["isCompiling"] = EditorApplication.isCompiling,
                ["isUpdating"] = EditorApplication.isUpdating,
                ["operation"] = EditorOperation.Status(),
                ["next"] = idle ? "Editor is idle; you can proceed." : "Poll wait_for_idle again after a moment."
            };
        }

        // ---- tags/layers ----------------------------------------------------- //

        private static SerializedObject TagManager()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0)
                throw new ToolException("tag_manager_missing", "ProjectSettings/TagManager.asset could not be loaded.");
            return new SerializedObject(assets[0]);
        }

        private static List<object> GetTags()
        {
            return UnityEditorInternal.InternalEditorUtility.tags
                .Select(t => (object)t).ToList();
        }

        private static ToolResult AddTag(IDictionary<string, object> p, bool idempotent)
        {
            string name = JsonUtil.Str(p, "tagName");
            if (string.IsNullOrWhiteSpace(name)) throw new ToolException("missing_param", "tagName is required.");

            if (UnityEditorInternal.InternalEditorUtility.tags.Contains(name))
            {
                if (idempotent) return new ToolResult($"Tag '{name}' already exists.", new Dictionary<string, object>(StringComparer.Ordinal)
                { ["tag"] = name, ["created"] = false });
                throw new ToolException("tag_exists", $"Tag '{name}' already exists.");
            }

            var so = TagManager();
            var tags = so.FindProperty("tags");
            tags.InsertArrayElementAtIndex(tags.arraySize);
            tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = name;
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();

            return new ToolResult($"Tag '{name}' created.", new Dictionary<string, object>(StringComparer.Ordinal)
            { ["tag"] = name, ["created"] = true });
        }

        private static ToolResult RemoveTag(IDictionary<string, object> p)
        {
            string name = JsonUtil.Str(p, "tagName");
            if (string.IsNullOrWhiteSpace(name)) throw new ToolException("missing_param", "tagName is required.");

            var so = TagManager();
            var tags = so.FindProperty("tags");
            for (int i = 0; i < tags.arraySize; i++)
            {
                if (tags.GetArrayElementAtIndex(i).stringValue != name) continue;
                tags.DeleteArrayElementAtIndex(i);
                so.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                return new ToolResult($"Tag '{name}' removed.", new Dictionary<string, object>(StringComparer.Ordinal)
                { ["tag"] = name, ["removed"] = true });
            }
            throw new ToolException("tag_not_found", $"Tag '{name}' does not exist.");
        }

        private static List<object> GetLayers()
        {
            var result = new List<object>();
            var so = TagManager();
            var layers = so.FindProperty("layers");
            for (int i = 0; i < layers.arraySize; i++)
            {
                string value = layers.GetArrayElementAtIndex(i).stringValue;
                if (string.IsNullOrEmpty(value)) continue;
                result.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["index"] = i,
                    ["name"] = value
                });
            }
            return result;
        }

        private static ToolResult AddLayer(IDictionary<string, object> p, bool idempotent)
        {
            string name = JsonUtil.Str(p, "layerName");
            if (string.IsNullOrWhiteSpace(name)) throw new ToolException("missing_param", "layerName is required.");

            var so = TagManager();
            var layers = so.FindProperty("layers");
            for (int i = 0; i < layers.arraySize; i++)
            {
                if (layers.GetArrayElementAtIndex(i).stringValue != name) continue;
                if (idempotent) return new ToolResult($"Layer '{name}' already exists.", new Dictionary<string, object>(StringComparer.Ordinal)
                { ["layer"] = name, ["index"] = i, ["created"] = false });
                throw new ToolException("layer_exists", $"Layer '{name}' already exists at index {i}.");
            }

            for (int i = 8; i < layers.arraySize; i++)
            {
                if (!string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue)) continue;
                layers.GetArrayElementAtIndex(i).stringValue = name;
                so.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                return new ToolResult($"Layer '{name}' created at index {i}.", new Dictionary<string, object>(StringComparer.Ordinal)
                { ["layer"] = name, ["index"] = i, ["created"] = true });
            }

            throw new ToolException("no_free_layer", "No free user layer slot (8-31). Remove a layer first.");
        }

        private static ToolResult RemoveLayer(IDictionary<string, object> p)
        {
            string name = JsonUtil.Str(p, "layerName");
            if (string.IsNullOrWhiteSpace(name)) throw new ToolException("missing_param", "layerName is required.");

            var so = TagManager();
            var layers = so.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++)
            {
                if (layers.GetArrayElementAtIndex(i).stringValue != name) continue;
                layers.GetArrayElementAtIndex(i).stringValue = string.Empty;
                so.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                return new ToolResult($"Layer '{name}' removed.", new Dictionary<string, object>(StringComparer.Ordinal)
                { ["layer"] = name, ["index"] = i, ["removed"] = true });
            }
            throw new ToolException("layer_not_found", $"Layer '{name}' does not exist (only user layers 8-31 can be removed).");
        }

        // ---- windows/tools --------------------------------------------------- //

        private static List<object> GetWindows()
        {
            var result = new List<object>();
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (window == null) continue;
                result.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["type"] = window.GetType().Name,
                    ["fullType"] = window.GetType().FullName,
                    ["title"] = window.titleContent != null ? window.titleContent.text : window.name,
                    ["hasFocus"] = window.hasFocus,
                    ["instanceID"] = UnityEngine.EntityId.ToULong(window.GetEntityId())
                });
            }
            return result;
        }

        private static ToolResult FocusWindow(IDictionary<string, object> p)
        {
            string wanted = JsonUtil.Str(p, "windowType");
            if (string.IsNullOrWhiteSpace(wanted)) throw new ToolException("missing_param", "windowType is required.");

            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (window == null) continue;
                var type = window.GetType();
                if (!string.Equals(type.Name, wanted, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(type.FullName, wanted, StringComparison.OrdinalIgnoreCase))
                    continue;

                window.Show();
                window.Focus();
                return new ToolResult($"Focused {type.Name}.", new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["type"] = type.Name,
                    ["focused"] = true
                });
            }

            throw new ToolException("window_not_found",
                $"No open editor window matches '{wanted}'. Call get_windows for the current list.");
        }

        private static ToolResult SetActiveTool(IDictionary<string, object> p)
        {
            string name = JsonUtil.Str(p, "toolName");
            if (string.IsNullOrWhiteSpace(name)) throw new ToolException("missing_param", "toolName is required.");

            if (!Enum.TryParse(name, true, out Tool tool))
                throw new ToolException("invalid_tool", $"Unknown tool '{name}'. Use Move, Rotate, Scale, View or Transform.");

            Tools.current = tool;
            return new ToolResult($"Active tool set to {Tools.current}.", new Dictionary<string, object>(StringComparer.Ordinal)
            { ["tool"] = Tools.current.ToString() });
        }
    }
}
