using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Codely.UnityMcp
{
    /// <summary>
    /// Static reference lookup: which scenes/prefabs depend on an asset, or which objects
    /// carry a component. Uses AssetDatabase dependency data for scenes/prefabs so it does
    /// not have to open them.
    ///
    /// Dynamic loading (Resources.Load, Addressables, string paths) is invisible here, and
    /// the response says so instead of reporting a misleading zero.
    /// </summary>
    public static class FindUsageTool
    {
        public static ToolResult Handle(IDictionary<string, object> p)
        {
            string path = JsonUtil.Str(p, "path");
            string typeName = JsonUtil.Str(p, "type");
            string guid = JsonUtil.Str(p, "guid");

            if (!string.IsNullOrEmpty(guid) && string.IsNullOrEmpty(path))
                path = AssetDatabase.GUIDToAssetPath(guid);

            if (string.IsNullOrEmpty(path) && string.IsNullOrEmpty(typeName))
                throw new ToolException("missing_param", "Provide 'path' (asset), 'guid', or 'type' (component class name).");

            if (!string.IsNullOrEmpty(path))
            {
                path = PathUtil.AssetsPath(path);
                if (AssetDatabase.LoadMainAssetAtPath(path) == null)
                    throw new ToolException("asset_not_found", $"Asset '{path}' does not exist.");

                if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    var mono = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                    var type = mono != null ? mono.GetClass() : null;
                    if (type == null)
                        throw new ToolException("type_not_compiled",
                            "The script's type is not compiled yet; recompile and retry.");
                    return UsageOfComponent(type, path);
                }

                return UsageOfAsset(path);
            }

            var resolved = ScriptAnalysis.FindScriptPathByType(typeName);
            if (string.IsNullOrEmpty(resolved))
                throw new ToolException("script_not_found",
                    $"No MonoScript named '{typeName}' under Assets/. Try project_map.");

            var monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(resolved);
            var componentType = monoScript != null ? monoScript.GetClass() : null;
            if (componentType == null)
                throw new ToolException("type_not_compiled", "The script's type is not compiled yet; recompile and retry.");
            return UsageOfComponent(componentType, resolved);
        }

        private static ToolResult UsageOfComponent(Type type, string scriptPath)
        {
            ScriptAnalysis.CollectUsage(type, 200, out var sceneObjects, out var prefabs);

            var data = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["target"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["kind"] = "component",
                    ["type"] = type.Name,
                    ["script"] = scriptPath,
                    ["guid"] = AssetDatabase.AssetPathToGUID(scriptPath)
                },
                ["used_by"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["scene_objects"] = sceneObjects,
                    ["prefabs"] = prefabs,
                    ["count"] = sceneObjects.Count + prefabs.Count
                },
                ["next"] = new List<object>
                {
                    "Inspect one instance: manage_gameobject.get_components with its path.",
                    "Change a value: manage_gameobject.set_component_property."
                }
            };

            AddDynamicNote(data, sceneObjects.Count + prefabs.Count);
            return new ToolResult($"'{type.Name}' is attached in {sceneObjects.Count + prefabs.Count} place(s).", data);
        }

        private static ToolResult UsageOfAsset(string assetPath)
        {
            var scenes = new List<object>();
            var prefabs = new List<object>();

            foreach (var sceneGuid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }))
            {
                string scenePath = AssetDatabase.GUIDToAssetPath(sceneGuid);
                if (string.IsNullOrEmpty(scenePath)) continue;
                if (!DependsOn(scenePath, assetPath)) continue;
                scenes.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["path"] = scenePath,
                    ["name"] = Path.GetFileNameWithoutExtension(scenePath)
                });
            }

            int inspected = 0;
            foreach (var prefabGuid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
            {
                if (inspected++ >= 300) break;
                string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuid);
                if (string.IsNullOrEmpty(prefabPath)) continue;
                if (!DependsOn(prefabPath, assetPath)) continue;
                prefabs.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["path"] = prefabPath,
                    ["name"] = Path.GetFileNameWithoutExtension(prefabPath)
                });
                if (prefabs.Count >= 100) break;
            }

            var type = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
            var data = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["target"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["kind"] = "asset",
                    ["path"] = assetPath,
                    ["guid"] = AssetDatabase.AssetPathToGUID(assetPath),
                    ["type"] = type != null ? type.Name : null
                },
                ["used_by"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["scenes"] = scenes,
                    ["prefabs"] = prefabs,
                    ["count"] = scenes.Count + prefabs.Count,
                    ["searched_prefabs"] = Math.Min(inspected, 300)
                },
                ["next"] = new List<object>
                {
                    "Change how it renders: manage_asset.modify with material/shader property names.",
                    "Find the objects that use it: open a listed scene, then manage_gameobject.find by component type."
                }
            };

            AddDynamicNote(data, scenes.Count + prefabs.Count);
            return new ToolResult($"{scenes.Count + prefabs.Count} static reference(s) found.", data);
        }

        private static bool DependsOn(string ownerPath, string assetPath)
        {
            try
            {
                var deps = AssetDatabase.GetDependencies(ownerPath, false);
                return deps.Any(dep => string.Equals(dep, assetPath, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        private static void AddDynamicNote(Dictionary<string, object> data, int count)
        {
            if (count > 0) return;
            data["hint"] =
                "No static reference found. Resources.Load / Addressables / string-based loading is not visible to AssetDatabase dependencies.";
        }
    }
}
