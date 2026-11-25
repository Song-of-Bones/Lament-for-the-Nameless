using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Codely.UnityMcp
{
    /// <summary>
    /// One-stop description of a script: what type it declares, what it exposes, whether it
    /// is a runtime component/asset, and where it is already attached. Replaces reading the
    /// .cs file and guessing which fields matter.
    /// </summary>
    public static class InspectScriptTool
    {
        public static ToolResult Handle(IDictionary<string, object> p)
        {
            string path = JsonUtil.Str(p, "path");
            string typeName = JsonUtil.Str(p, "type");

            if (string.IsNullOrEmpty(path) && string.IsNullOrEmpty(typeName))
                throw new ToolException("missing_param",
                    "Provide 'path' (a .cs asset path) or 'type' (a component/class name).");

            if (string.IsNullOrEmpty(path))
            {
                path = ScriptAnalysis.FindScriptPathByType(typeName);
                if (string.IsNullOrEmpty(path))
                    throw new ToolException("script_not_found",
                        $"No MonoScript named '{typeName}' was found under Assets/. Try project_map to list custom components.");
            }

            path = PathUtil.AssetsPath(path);
            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                throw new ToolException("not_a_script",
                    $"'{path}' is not a C# script. Use manage_asset.get_info for other asset types.");

            if (AssetDatabase.LoadAssetAtPath<MonoScript>(path) == null)
                throw new ToolException("script_not_found", $"MonoScript '{path}' could not be loaded.");

            var info = ScriptAnalysis.Analyze(path);
            if (info == null)
                throw new ToolException("script_unreadable", $"'{path}' could not be read.");

            bool includeUsage = JsonUtil.Bool(p, "includeUsage", true);
            int maxPrefabs = Mathf.Clamp(JsonUtil.Int(p, "maxPrefabs", 150), 0, 500);

            var data = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["script"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["path"] = info.Path,
                    ["type"] = info.TypeName,
                    ["base"] = info.BaseType,
                    ["namespace"] = info.Namespace,
                    ["summary"] = info.Summary,
                    ["kind"] = KindOf(info),
                    ["role"] = ScriptAnalysis.RoleOf(info),
                    ["is_editor_script"] = info.IsEditorScript,
                    ["lifecycle"] = info.Lifecycle.Cast<object>().ToList(),
                    ["fields"] = info.Fields.Take(40).Select(Field).ToList(),
                    ["methods"] = info.Methods.Take(40).Select(Method).ToList(),
                    ["field_count"] = info.Fields.Count,
                    ["method_count"] = info.Methods.Count
                }
            };

            if (includeUsage)
            {
                var mono = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                var type = mono != null ? mono.GetClass() : null;
                if (type != null)
                {
                    ScriptAnalysis.CollectUsage(type, maxPrefabs, out var sceneObjects, out var prefabs);
                    data["used_by"] = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["scene_objects"] = sceneObjects,
                        ["prefabs"] = prefabs,
                        ["count"] = sceneObjects.Count + prefabs.Count
                    };
                }
                else
                {
                    data["used_by"] = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["count"] = 0,
                        ["note"] = "The type is not compiled yet, so attachment lookup is unavailable. Recompile and retry."
                    };
                }
            }

            data["next"] = new List<object>
            {
                "Read live values: manage_gameobject.get_components on an object listed in used_by.scene_objects.",
                "Change a field: manage_gameobject.set_component_property with a field name from script.fields.",
                "Find the script's asset references: find_usage with this script path."
            };

            return new ToolResult($"Inspected '{info.TypeName}'.", data);
        }

        private static object Field(ScriptField field)
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["name"] = field.Name,
                ["type"] = field.Type,
                ["settable"] = true,
                ["set_with"] = "manage_gameobject.set_component_property"
            };
        }

        private static object Method(ScriptMethod method)
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["name"] = method.Name,
                ["returns"] = method.Returns,
                ["params"] = method.Params
            };
        }

        private static string KindOf(ScriptInfo info)
        {
            if (info.IsEditorScript) return "editor";
            if (info.IsScriptableObject) return "scriptable_object";
            if (info.IsMonoBehaviour) return "monobehaviour";
            return "plain_class";
        }
    }
}
