using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Codely.UnityMcp
{
    /// <summary>
    /// A semantic map of the project. Instead of dumping file counts, it answers the
    /// questions an agent actually has: which folders hold what, which scenes exist and
    /// what they are for, which custom components exist, what they expose, and where they
    /// are already used.
    ///
    /// This is the main "do not go read the source tree" tool.
    /// </summary>
    public static class ProjectMapTool
    {
        private static Dictionary<string, object> _cache;
        private static double _cacheAt = -1000;
        private const double CacheSeconds = 30;

        public static ToolResult Handle(IDictionary<string, object> p)
        {
            bool force = JsonUtil.Bool(p, "force", false);
            int maxComponents = Mathf.Clamp(JsonUtil.Int(p, "maxComponents", 40), 1, 200);
            bool includeFolders = JsonUtil.Bool(p, "includeFolders", true);
            bool includePrefabUsage = JsonUtil.Bool(p, "includePrefabUsage", false);

            double now = EditorApplication.timeSinceStartup;
            if (!force && _cache != null && now - _cacheAt < CacheSeconds)
                return new ToolResult("Project map (cached).", _cache);

            var sceneIndex = BuildSceneComponentIndex();

            var data = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["project"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["root"] = McpBridgeServer.ProjectRoot,
                    ["unity"] = Application.unityVersion,
                    ["product"] = Application.productName,
                    ["company"] = Application.companyName,
                    ["srp"] = DescribePipeline(),
                    ["active_scene"] = SceneManager.GetActiveScene().path
                },
                ["scenes"] = BuildScenes(),
                ["components"] = BuildComponents(maxComponents, sceneIndex, includePrefabUsage),
                ["namespaces"] = BuildNamespaces(),
                ["how_to_use"] = new List<object>
                {
                    "components[].type is the exact component name for manage_gameobject.get_components / set_component_property.",
                    "components[].script is the asset path for inspect_script.",
                    "components[].fields[].name is the serialized field name to pass to set_component_property.",
                    "For a concrete object, call manage_gameobject.find first: it returns a ref you can pass back."
                }
            };

            if (includeFolders) data["folders"] = BuildFolders();

            data["entry_points"] = BuildEntryPoints(data["components"] as List<object>);
            data["_map_version"] = 1;

            _cache = data;
            _cacheAt = now;
            return new ToolResult("Project map collected.", data);
        }

        // ---- folders ---------------------------------------------------------- //

        private static List<object> BuildFolders()
        {
            var result = new List<object>();
            string assets = Application.dataPath;
            if (!Directory.Exists(assets)) return result;

            foreach (var dir in Directory.GetDirectories(assets).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var info = new DirectoryInfo(dir);
                if (info.Name.StartsWith(".", StringComparison.Ordinal)) continue;

                var files = info.GetFiles("*", SearchOption.AllDirectories);
                var kinds = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var file in files)
                {
                    string kind = KindOf(file.Extension);
                    kinds[kind] = kinds.TryGetValue(kind, out object existing)
                        ? Convert.ToInt32(existing) + 1
                        : 1;
                }

                result.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["path"] = "Assets/" + info.Name,
                    ["role"] = RoleForFolder(info.Name),
                    ["files"] = files.Length,
                    ["kinds"] = kinds
                });

                if (result.Count >= 40) break;
            }
            return result;
        }

        private static string KindOf(string extension)
        {
            switch ((extension ?? string.Empty).ToLowerInvariant())
            {
                case ".cs": return "script";
                case ".unity": return "scene";
                case ".prefab": return "prefab";
                case ".mat": return "material";
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".tga":
                case ".psd": return "texture";
                case ".anim":
                case ".controller": return "animation";
                case ".asset": return "asset";
                case ".wav":
                case ".mp3":
                case ".ogg": return "audio";
                case ".shader":
                case ".hlsl":
                case ".cginc": return "shader";
                case ".ttf":
                case ".otf": return "font";
                default: return "other";
            }
        }

        private static string RoleForFolder(string name)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("scene") || name.Contains("场景")) return "scenes";
            if (n.Contains("editor")) return "editor_tools";
            if (n.Contains("resources")) return "runtime_resources";
            if (n.Contains("prefab") || name.Contains("预制")) return "prefabs";
            if (n.Contains("script") || n.Contains("code") || name.Contains("脚本") || name.Contains("代码")) return "code";
            if (n.Contains("art") || n.Contains("texture") || n.Contains("sprite") || n.Contains("icon") ||
                name.Contains("美术") || name.Contains("素材") || name.Contains("图")) return "art";
            if (n.Contains("anim") || name.Contains("动画")) return "animation";
            if (n.Contains("audio") || n.Contains("sound") || name.Contains("音")) return "audio";
            if (n.Contains("setting") || n.Contains("config") || name.Contains("设置")) return "settings";
            if (n.Contains("plugin") || n.Contains("third")) return "plugins";
            if (n.Contains("shader")) return "shaders";
            if (n.Contains("font") || name.Contains("字体")) return "fonts";
            if (name.Contains("分辨率") || n.Contains("resolution")) return "ui_support";
            if (name.Contains("密钥") || name.Contains("防逆向") || n.Contains("security") || n.Contains("anti")) return "security";
            if (name.Contains("文字") || n.Contains("text")) return "text";
            if (name.Contains("资源") || n.Contains("resource")) return "runtime_resources";
            return "other";
        }

        // ---- scenes ------------------------------------------------------------ //

        private static List<object> BuildScenes()
        {
            var build = EditorBuildSettings.scenes;
            var buildIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < build.Length; i++)
                buildIndex[build[i].path] = i;

            var result = new List<object>();
            foreach (var guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                string name = Path.GetFileNameWithoutExtension(path);
                result.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["path"] = path,
                    ["name"] = name,
                    ["role"] = RoleForScene(name),
                    ["build_index"] = buildIndex.TryGetValue(path, out int index) ? index : (object)null,
                    ["in_build"] = buildIndex.ContainsKey(path),
                    ["loaded"] = SceneManager.GetActiveScene().path == path
                });
            }
            return result.OrderBy(x => Convert.ToString(((Dictionary<string, object>)x)["path"], System.Globalization.CultureInfo.InvariantCulture),
                StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string RoleForScene(string name)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("login") || name.Contains("登录")) return "login";
            if (n.Contains("lobby") || n.Contains("hall") || name.Contains("大厅")) return "lobby";
            if (n.Contains("setting") || name.Contains("设置")) return "settings";
            if (n.Contains("menu") || name.Contains("菜单")) return "menu";
            if (n.Contains("game") || n.Contains("level") || n.Contains("play") || name.Contains("游戏")) return "gameplay";
            return "other";
        }

        // ---- components -------------------------------------------------------- //

        private static List<object> BuildComponents(int maxComponents, Dictionary<string, List<object>> sceneIndex, bool includePrefabUsage)
        {
            var result = new List<object>();
            var prefabUsage = includePrefabUsage ? BuildPrefabUsageIndex() : null;

            foreach (var guid in AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" }))
            {
                if (result.Count >= maxComponents) break;
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                if (path.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                var mono = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                var type = mono != null ? mono.GetClass() : null;
                if (type == null) continue;
                if (!typeof(MonoBehaviour).IsAssignableFrom(type) && !typeof(ScriptableObject).IsAssignableFrom(type)) continue;

                var info = ScriptAnalysis.Analyze(path);
                if (info == null) continue;

                sceneIndex.TryGetValue(type.Name, out var usedInScene);
                var entry = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["type"] = type.Name,
                    ["script"] = path,
                    ["source"] = IsThirdParty(path) ? "third_party" : "project",
                    ["base"] = info.BaseType,
                    ["namespace"] = info.Namespace,
                    ["role"] = ScriptAnalysis.RoleOf(info),
                    ["summary"] = info.Summary,
                    ["fields"] = info.Fields.Take(12).Select(f => (object)new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["name"] = f.Name,
                        ["type"] = f.Type
                    }).ToList(),
                    ["lifecycle"] = info.Lifecycle.Cast<object>().ToList(),
                    ["used_in_scene"] = usedInScene != null ? usedInScene.Take(8).ToList() : new List<object>(),
                    ["used_count"] = usedInScene != null ? usedInScene.Count : 0
                };

                if (prefabUsage != null && prefabUsage.TryGetValue(type.Name, out int prefabCount))
                    entry["used_in_prefabs"] = prefabCount;

                result.Add(entry);
            }

            return result
                .OfType<Dictionary<string, object>>()
                .OrderBy(entry => Convert.ToString(entry["source"]) == "project" ? 0 : 1)
                .ThenByDescending(entry => Convert.ToInt32(entry["used_count"]))
                .Select(entry => (object)entry)
                .ToList();
        }

        private static bool IsThirdParty(string path)
        {
            return path.IndexOf("/cn.tuanjie.", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   path.IndexOf("/TJGenerators/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   path.IndexOf("/Plugins/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   path.IndexOf("/ThirdParty/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Dictionary<string, List<object>> BuildSceneComponentIndex()
        {
            var index = new Dictionary<string, List<object>>(StringComparer.Ordinal);

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid()) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var component in root.GetComponentsInChildren<Component>(true))
                    {
                        if (component == null) continue;
                        string typeName = component.GetType().Name;
                        if (!index.TryGetValue(typeName, out var list))
                        {
                            list = new List<object>();
                            index[typeName] = list;
                        }
                        list.Add(ManageGameObjectTool.HierarchyPath(component.transform));
                    }
                }
            }
            return index;
        }

        private static Dictionary<string, int> BuildPrefabUsageIndex()
        {
            var usage = new Dictionary<string, int>(StringComparer.Ordinal);
            int inspected = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
            {
                if (inspected++ >= 150) break;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab == null) continue;
                foreach (var component in prefab.GetComponentsInChildren<Component>(true))
                {
                    if (component == null) continue;
                    string typeName = component.GetType().Name;
                    usage[typeName] = usage.TryGetValue(typeName, out int count) ? count + 1 : 1;
                }
            }
            return usage;
        }

        private static List<object> BuildNamespaces()
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var guid in AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || IsThirdParty(path)) continue;

                var mono = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                var type = mono != null ? mono.GetClass() : null;
                string ns = type != null && !string.IsNullOrEmpty(type.Namespace) ? type.Namespace : "<global>";
                counts[ns] = counts.TryGetValue(ns, out int count) ? count + 1 : 1;
            }

            return counts.OrderByDescending(kv => kv.Value)
                .Take(20)
                .Select(kv => (object)new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["namespace"] = kv.Key,
                    ["scripts"] = kv.Value
                })
                .ToList();
        }

        private static List<object> BuildEntryPoints(List<object> components)
        {
            var result = new List<object>();
            if (components == null) return result;

            foreach (var item in components)
            {
                var entry = item as Dictionary<string, object>;
                if (entry == null) continue;
                string role = Convert.ToString(entry["role"]);
                if (Convert.ToString(entry["source"]) == "third_party") continue;
                if (role != "manager" && role != "data_asset") continue;
                result.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["type"] = entry["type"],
                    ["script"] = entry["script"],
                    ["role"] = role,
                    ["why"] = "Name suggests it owns state or flow; inspect it first to understand the feature."
                });
                if (result.Count >= 15) break;
            }
            return result;
        }

        private static string DescribePipeline()
        {
            var pipeline = QualitySettings.renderPipeline ?? UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
            return pipeline == null ? "built-in" : pipeline.GetType().Name + " (" + pipeline.name + ")";
        }
    }
}
