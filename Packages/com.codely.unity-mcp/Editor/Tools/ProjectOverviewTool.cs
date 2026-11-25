using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Codely.UnityMcp
{
    /// <summary>
    /// The "read this first" tool. An agent that knows nothing about the project can call
    /// this once and learn the engine version, render pipeline, scene layout, package set,
    /// asset mix, play/compile state and console health — enough to pick the next tool
    /// without guessing from file names.
    /// </summary>
    public static class ProjectOverviewTool
    {
        private static Dictionary<string, object> _cache;
        private static double _cacheAt = -1000;
        private const double CacheSeconds = 20;

        public static ToolResult Handle(IDictionary<string, object> p)
        {
            bool includeCounts = JsonUtil.Bool(p, "includeAssetCounts", true);
            bool force = JsonUtil.Bool(p, "force", false);

            double now = EditorApplication.timeSinceStartup;
            if (!force && !includeCounts && _cache != null && now - _cacheAt < CacheSeconds)
                return new ToolResult("Project overview (cached).", _cache);

            var scene = SceneManager.GetActiveScene();
            var loaded = new List<object>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                loaded.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["name"] = s.name,
                    ["path"] = s.path,
                    ["isActive"] = s == scene,
                    ["isDirty"] = s.isDirty,
                    ["rootCount"] = s.rootCount
                });
            }

            var buildScenes = new List<object>();
            var build = EditorBuildSettings.scenes;
            for (int i = 0; i < build.Length; i++)
            {
                buildScenes.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["index"] = i,
                    ["path"] = build[i].path,
                    ["enabled"] = build[i].enabled,
                    ["name"] = Path.GetFileNameWithoutExtension(build[i].path)
                });
            }

            var data = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["bridge"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["server"] = "codely-unity-mcp",
                    ["package_version"] = McpBridgeServer.PackageVersion,
                    ["protocol"] = McpBridgeServer.ProtocolVersion,
                    ["port"] = McpBridgeServer.Port,
                    ["heartbeat_file"] = "Temp/.codely-unity-mcp.json",
                    ["next_step"] = "Call list_tools for the full capability catalog."
                },
                ["project"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["root"] = McpBridgeServer.ProjectRoot,
                    ["data_path"] = Application.dataPath,
                    ["product_name"] = Application.productName,
                    ["company_name"] = Application.companyName,
                    ["unity_version"] = Application.unityVersion,
                    ["platform"] = Application.platform.ToString(),
                    ["render_pipeline"] = DescribePipeline(),
                    ["color_space"] = PlayerSettings.colorSpace.ToString(),
                    ["scripting_backend"] = PlayerSettings.GetScriptingBackend(
                        UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(
                            BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget))).ToString()
                },
                ["editor"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["play_mode"] = EditorApplication.isPlaying ? (EditorApplication.isPaused ? "paused" : "playing") : "stopped",
                    ["is_compiling"] = EditorApplication.isCompiling,
                    ["is_updating"] = EditorApplication.isUpdating,
                    ["active_tool"] = Tools.current.ToString(),
                    ["last_operation"] = EditorOperation.Status()
                },
                ["active_scene"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["name"] = scene.name,
                    ["path"] = scene.path,
                    ["is_dirty"] = scene.isDirty,
                    ["root_count"] = scene.rootCount
                },
                ["loaded_scenes"] = loaded,
                ["build_scenes"] = buildScenes,
                ["console"] = ConsoleHealth(),
                ["top_level_folders"] = TopLevelFolders(),
                ["packages"] = Packages(),
                ["capabilities_hint"] = "This bridge exposes project_overview, list_tools, manage_editor, manage_scene, " +
                                        "manage_gameobject, manage_asset, read_console, execute_menu_item, manage_package, " +
                                        "manage_gameview, manage_guid, manage_screenshot and exec_editor_script."
            };

            if (includeCounts)
                data["asset_counts"] = AssetCounts();

            data["suggested_next_steps"] = SuggestedNextSteps(scene, data);

            _cache = data;
            _cacheAt = now;
            return new ToolResult("Project overview collected.", data);
        }

        private static string DescribePipeline()
        {
            var pipeline = QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline;
            if (pipeline == null) return "built-in";
            string name = pipeline.GetType().Name;
            if (name.IndexOf("Universal", StringComparison.OrdinalIgnoreCase) >= 0) return "urp (" + pipeline.name + ")";
            if (name.IndexOf("HD", StringComparison.OrdinalIgnoreCase) >= 0) return "hdrp (" + pipeline.name + ")";
            return name + " (" + pipeline.name + ")";
        }

        private static Dictionary<string, object> ConsoleHealth()
        {
            var console = ConsoleBuffer.Read(new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["types"] = new List<object> { "error", "warning" },
                ["count"] = 10,
                ["format"] = "plain",
                ["collapse"] = true
            });

            var result = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["error_count"] = console["error_count"],
                ["warning_count"] = console["warning_count"],
                ["captured_total"] = console["captured_total"],
                ["recent"] = console["entries"]
            };
            return result;
        }

        private static List<object> TopLevelFolders()
        {
            var folders = new List<object>();
            string assets = Application.dataPath;
            if (!Directory.Exists(assets)) return folders;

            foreach (var dir in Directory.GetDirectories(assets).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var info = new DirectoryInfo(dir);
                if (info.Name.StartsWith(".", StringComparison.Ordinal)) continue;
                folders.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["name"] = info.Name,
                    ["path"] = "Assets/" + info.Name,
                    ["files"] = SafeCount(info.GetFiles("*", SearchOption.AllDirectories)),
                    ["modified_utc"] = info.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture)
                });
                if (folders.Count >= 40) break;
            }
            return folders;
        }

        private static int SafeCount(FileInfo[] files) => files == null ? 0 : files.Length;

        private static List<object> Packages()
        {
            var result = new List<object>();
            try
            {
                var registered = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
                foreach (var pkg in registered.OrderBy(x => x.name, StringComparer.OrdinalIgnoreCase))
                {
                    if (pkg.name.StartsWith("com.unity.modules.", StringComparison.Ordinal)) continue;
                    result.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["name"] = pkg.name,
                        ["version"] = pkg.version,
                        ["source"] = pkg.source.ToString()
                    });
                    if (result.Count >= 80) break;
                }
            }
            catch (Exception ex)
            {
                result.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["error"] = "Package enumeration failed: " + ex.Message
                });
            }
            return result;
        }

        private static Dictionary<string, object> AssetCounts()
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["scenes"] = CountAssets("t:Scene"),
                ["scripts"] = CountAssets("t:MonoScript"),
                ["project_scripts"] = CountProjectScripts(),
                ["prefabs"] = CountAssets("t:Prefab"),
                ["materials"] = CountAssets("t:Material"),
                ["textures"] = CountAssets("t:Texture2D"),
                ["animation_clips"] = CountAssets("t:AnimationClip"),
                ["scriptable_objects"] = CountAssets("t:ScriptableObject")
            };
        }

        /// <summary>
        /// Scripts owned by this project, excluding bundled third-party trees. The raw
        /// MonoScript count is dominated by TJGenerators and misleads readers about size.
        /// </summary>
        private static int CountProjectScripts()
        {
            try
            {
                int count = 0;
                foreach (var guid in AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path)) continue;
                    if (path.IndexOf("/cn.tuanjie.", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (path.IndexOf("/TJGenerators/", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (path.IndexOf("/Welcome/", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    count++;
                }
                return count;
            }
            catch { return -1; }
        }

        private static int CountAssets(string filter)
        {
            try { return AssetDatabase.FindAssets(filter, new[] { "Assets" }).Length; }
            catch { return -1; }
        }

        private static List<object> SuggestedNextSteps(Scene scene, Dictionary<string, object> data)
        {
            var steps = new List<object>();
            if (scene.path == null || string.IsNullOrEmpty(scene.path))
                steps.Add("No scene is open. Use manage_scene.load with a build scene path, or manage_scene.create.");
            else
                steps.Add($"Inspect the open scene with manage_scene.get_hierarchy (active: {scene.path}).");

            if (EditorApplication.isPlaying)
                steps.Add("The editor is in Play Mode. Project writes are blocked; call manage_editor.stop to leave it.");

            var console = data["console"] as Dictionary<string, object>;
            int errors = console != null ? Convert.ToInt32(console["error_count"], CultureInfo.InvariantCulture) : 0;
            if (errors > 0)
                steps.Add("Console has errors. Call read_console.get with types ['error'] to see them before editing.");

            steps.Add("Use manage_asset.search to locate art/prefabs, and manage_gameobject.find before modifying objects.");
            steps.Add("After editing code or assets, run manage_editor.refresh or request_compile, then read_console.");
            return steps;
        }
    }

    /// <summary>
    /// Tracks the single long-running editor operation (play/stop/refresh/compile) across
    /// domain reloads. SessionState survives the reload, so a client that reconnects can
    /// still ask "is it done?" instead of starting the operation twice.
    /// </summary>
    public static class EditorOperation
    {
        private const string Key = "Codely.UnityMcp.Operation";

        public static void Begin(string name)
        {
            var state = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["name"] = name,
                ["started_at"] = EditorApplication.timeSinceStartup
            };
            SessionState.SetString(Key, MiniJson.Serialize(state));
        }

        public static void Clear() => SessionState.EraseString(Key);

        private static string CurrentName
        {
            get
            {
                string raw = SessionState.GetString(Key, null);
                if (string.IsNullOrEmpty(raw)) return null;
                try
                {
                    var dict = MiniJson.Deserialize(raw) as IDictionary<string, object>;
                    return JsonUtil.Str(dict, "name");
                }
                catch { return null; }
            }
        }

        private static double StartedAt
        {
            get
            {
                string raw = SessionState.GetString(Key, null);
                if (string.IsNullOrEmpty(raw)) return 0;
                try
                {
                    var dict = MiniJson.Deserialize(raw) as IDictionary<string, object>;
                    return JsonUtil.Float(dict, "started_at");
                }
                catch { return 0; }
            }
        }

        public static Dictionary<string, object> Status()
        {
            string name = CurrentName;
            if (string.IsNullOrEmpty(name))
                return new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["state"] = "idle",
                    ["operation"] = null
                };

            double elapsed = EditorApplication.timeSinceStartup - StartedAt;
            string state;
            string hint = null;

            switch (name)
            {
                case "play":
                    if (EditorApplication.isPlaying) { state = "done"; Clear(); }
                    else if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                        state = "running";
                    else if (elapsed > 5) { state = "failed"; hint = "Play Mode did not start. Check read_console for compile errors."; Clear(); }
                    else state = "running";
                    break;
                case "stop":
                    if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling)
                    { state = "done"; Clear(); }
                    else state = "running";
                    break;
                case "refresh":
                case "compile":
                    if (EditorApplication.isCompiling || EditorApplication.isUpdating) state = "running";
                    else if (elapsed < 0.5) state = "running";
                    else { state = "done"; Clear(); }
                    break;
                case "package":
                    if (elapsed < 3) state = "running";
                    else { state = "done"; Clear(); }
                    break;
                default:
                    if (elapsed > 0.5) { state = "done"; Clear(); }
                    else state = "running";
                    break;
            }

            var result = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["state"] = state,
                ["operation"] = name,
                ["elapsed_seconds"] = Math.Round(elapsed, 2),
                ["is_playing"] = EditorApplication.isPlaying,
                ["is_compiling"] = EditorApplication.isCompiling,
                ["is_updating"] = EditorApplication.isUpdating
            };
            if (hint != null) result["hint"] = hint;
            if (state == "running") result["next"] = "Poll manage_editor.get_operation again in a moment.";
            if (state == "done") result["next"] = "Operation finished. Read the console if you expected diagnostics.";
            return result;
        }
    }
}
