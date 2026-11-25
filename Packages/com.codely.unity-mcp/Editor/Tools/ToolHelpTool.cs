using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Codely.UnityMcp
{
    /// <summary>
    /// Per-action help: exact parameters, a copy-pasteable example, what the response
    /// contains, and the errors this action commonly returns.
    ///
    /// This exists because a catalog alone still leaves an agent guessing how to shape a
    /// call. `tool_help` answers "show me one working call" without reading source.
    /// </summary>
    public static class ToolHelpTool
    {
        private static readonly Dictionary<string, string> Returns = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["project_overview"] = "project{root,unity,srp,product}, editor{play_mode,is_compiling}, active_scene{name,path,is_dirty}, build_scenes[], asset_counts{scenes,scripts,prefabs,...}, console{error_count,warning_count,recent[]}, suggested_next_steps[]",
            ["project_map"] = "scenes[]{path,role,build_index}, components[]{type,script,base,role,summary,fields[],used_in_scene[],used_count}, folders[]{path,role,kinds}, namespaces[], entry_points[]",
            ["list_tools"] = "tool_count, tools[]{type,summary,when_to_use,actions{name:{description,params[]}},notes[],examples[]}",
            ["tool_help"] = "tool, action, description, params[]{name,type,required,default,description}, example{type,params}, returns, common_errors[]{code,fix}",
            ["inspect_script"] = "script{path,type,base,role,summary,lifecycle[],fields[]{name,type},methods[]{name,returns,params}}, used_by{scene_objects[],prefabs[],count}",
            ["find_usage"] = "target{kind,path|type,guid}, used_by{scenes[]|scene_objects[],prefabs[],count}, hint (dynamic loading is invisible)",
            ["manage_editor"] = "get_state: playMode,isCompiling,isUpdating,activeScene{name,path,isDirty},selection{objects[]},console{error_count,warning_count},operation{state,operation,elapsed_seconds}",
            ["manage_scene"] = "get_hierarchy: scenes[]{name,path,roots[]{name,path,ref,role,components[],children[]}}, nodeCount, truncated. load/save/create: scene{path}, savedPrevious/created",
            ["manage_gameobject"] = "find: objects[]{name,path,ref{kind,instance_id,pass_as},role,components[]}. get_components: components[]{type,ref,properties{name:value},field_schema{name:{type,settable,setter,enum_values}}}",
            ["manage_asset"] = "search: items[]{path,name,type,ref,usage_count?}, total, pageCount. get_info: path,guid,type,size_bytes,dependencies[],used_by?",
            ["read_console"] = "entries[] (plain strings or detailed objects), error_count, warning_count, diagnosis[]{matched,likely_cause,fix}, truncated",
            ["execute_menu_item"] = "executed: menu_path,executed. get_available_menus: menus[], note",
            ["manage_package"] = "list_packages: packages[]{name,version,source}. install/remove: status=started then operation{state}",
            ["manage_gameview"] = "get_resolution: width,height,approximate. list_resolutions: resolutions[]{index,width,height,label}",
            ["manage_guid"] = "asset_path, guid, matches, expected",
            ["manage_screenshot"] = "absolute_path, relative_path, width, height, bytes, camera, image_base64 (inline image content in MCP)",
            ["exec_editor_script"] = "summary, result (normalized JSON value), returns (CLR type), logs[]{type,message}, exception on failure"
        };

        private static readonly Dictionary<string, string[][]> Errors = new Dictionary<string, string[][]>(StringComparer.Ordinal)
        {
            ["manage_gameobject"] = new[]
            {
                new[] { "blocked_in_play_mode", "Call manage_editor.stop first; reads still work in Play Mode." },
                new[] { "object_not_found", "Use find with searchTerm, or get_hierarchy to inspect the scene." },
                new[] { "property_not_found", "Take the exact name from get_components.field_schema." },
                new[] { "component_not_found", "Take the exact type from get_components.components[].type." }
            },
            ["manage_asset"] = new[]
            {
                new[] { "asset_not_found", "Use search to get the exact path." },
                new[] { "property_not_found", "For materials use shader names (_Color, _Glossiness) or aliases color/metallic/smoothness." },
                new[] { "no_changes", "Values already matched; nothing was written." }
            },
            ["manage_scene"] = new[]
            {
                new[] { "scene_not_found", "Use project_map.scenes or manage_asset.search filterType Scene for exact paths." },
                new[] { "ambiguous_scene", "Pass an explicit path ending in .unity." }
            },
            ["manage_editor"] = new[]
            {
                new[] { "blocked_in_play_mode", "Stop Play Mode before refresh/compile." },
                new[] { "compiling", "Wait for get_operation state=done." }
            },
            ["inspect_script"] = new[]
            {
                new[] { "script_not_found", "Use project_map.components[].script for exact paths." },
                new[] { "not_a_script", "Use manage_asset.get_info for non-script assets." }
            },
            ["find_usage"] = new[]
            {
                new[] { "asset_not_found", "Resolve the path with manage_asset.search or project_map." },
                new[] { "type_not_compiled", "Fix compile errors, then retry." }
            },
            ["exec_editor_script"] = new[]
            {
                new[] { "compile_error", "Read data.errors[].line and fix the snippet; body is wrapped in Run()." },
                new[] { "script_error", "The snippet threw; read data.stackTrace." }
            }
        };

        public static ToolResult Handle(IDictionary<string, object> p)
        {
            string tool = JsonUtil.Str(p, "tool");
            string action = JsonUtil.Str(p, "action");

            var catalogData = ToolCatalog.Describe().Data as Dictionary<string, object>;
            var tools = catalogData != null ? catalogData["tools"] as List<object> : null;
            if (tools == null)
                throw new ToolException("catalog_unavailable", "The tool catalog could not be built.");

            if (string.IsNullOrEmpty(tool))
            {
                var summary = tools.OfType<Dictionary<string, object>>()
                    .Select(t => (object)new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["type"] = t["type"],
                        ["summary"] = t["summary"],
                        ["when_to_use"] = t["when_to_use"],
                        ["action_count"] = ((Dictionary<string, object>)t["actions"]).Count
                    })
                    .ToList();

                return new ToolResult("Tool index.", new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["tools"] = summary,
                    ["next"] = "Call tool_help with { tool, action } to get an example and the response shape."
                });
            }

            var descriptor = tools.OfType<Dictionary<string, object>>()
                .FirstOrDefault(t => string.Equals(Convert.ToString(t["type"]), tool, StringComparison.OrdinalIgnoreCase));
            if (descriptor == null)
                throw new ToolException("unknown_tool_type",
                    $"Unknown tool '{tool}'. Call tool_help without parameters to list tools.");

            var actions = descriptor["actions"] as Dictionary<string, object>;
            if (actions == null || actions.Count == 0)
                throw new ToolException("no_actions", $"'{tool}' declares no actions.");

            if (string.IsNullOrEmpty(action))
            {
                var entries = new List<object>();
                foreach (var kv in actions)
                {
                    var actionDescriptor = kv.Value as Dictionary<string, object>;
                    entries.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["action"] = kv.Key,
                        ["description"] = actionDescriptor != null ? actionDescriptor["description"] : null,
                        ["example"] = BuildExample(tool, kv.Key, actionDescriptor),
                        ["params"] = actionDescriptor != null ? actionDescriptor["params"] : null
                    });
                }

                return new ToolResult($"Actions for '{tool}'.", new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["tool"] = tool,
                    ["summary"] = descriptor["summary"],
                    ["when_to_use"] = descriptor["when_to_use"],
                    ["returns"] = Returns.TryGetValue(tool, out var returns) ? returns : null,
                    ["actions"] = entries,
                    ["common_errors"] = ErrorsFor(tool),
                    ["next"] = "Call tool_help with { tool, action } for one action's exact shape."
                });
            }

            if (!actions.TryGetValue(action, out object rawAction))
                throw new ToolException("unknown_action",
                    $"'{tool}' has no action '{action}'. Actions: {string.Join(", ", actions.Keys)}.");

            var descriptorForAction = rawAction as Dictionary<string, object>;
            return new ToolResult($"{tool}.{action}", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["tool"] = tool,
                ["action"] = action,
                ["description"] = descriptorForAction != null ? descriptorForAction["description"] : null,
                ["params"] = descriptorForAction != null ? descriptorForAction["params"] : null,
                ["example"] = BuildExample(tool, action, descriptorForAction),
                ["returns"] = Returns.TryGetValue(tool, out var shape) ? shape : null,
                ["common_errors"] = ErrorsFor(tool),
                ["notes"] = descriptor["notes"],
                ["next"] = "Send the example as-is, then replace values. Every response carries _meta{play_mode,can_write,blockers}."
            });
        }

        private static List<object> ErrorsFor(string tool)
        {
            if (!Errors.TryGetValue(tool, out var pairs)) return new List<object>();
            return pairs.Select(pair => (object)new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["code"] = pair[0],
                ["fix"] = pair[1]
            }).ToList();
        }

        private static Dictionary<string, object> BuildExample(string tool, string action,
            Dictionary<string, object> descriptor)
        {
            var parameters = new Dictionary<string, object>(StringComparer.Ordinal) { ["action"] = action };
            var paramList = descriptor != null ? descriptor["params"] as List<object> : null;
            if (paramList != null)
            {
                foreach (var item in paramList)
                {
                    var param = item as Dictionary<string, object>;
                    if (param == null) continue;
                    bool required = param["required"] is bool flag && flag;
                    if (!required) continue;

                    string name = Convert.ToString(param["name"], CultureInfo.InvariantCulture);
                    string type = Convert.ToString(param["type"], CultureInfo.InvariantCulture);
                    parameters[name] = SampleValue(type, param);
                }
            }

            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["type"] = tool,
                ["params"] = parameters
            };
        }

        private static object SampleValue(string type, Dictionary<string, object> param)
        {
            string name = Convert.ToString(param["name"], CultureInfo.InvariantCulture) ?? string.Empty;
            switch (type)
            {
                case "int": return 1;
                case "float": return 1.0;
                case "bool": return true;
                case "vec3": return new List<object> { 0, 0, 0 };
                case "json": return new Dictionary<string, object>();
                case "path":
                    if (name.IndexOf("scene", StringComparison.OrdinalIgnoreCase) >= 0) return "Assets/Scenes/Main.unity";
                    if (name.IndexOf("prefab", StringComparison.OrdinalIgnoreCase) >= 0) return "Assets/Prefabs/Item.prefab";
                    return "Assets/Path/Asset.ext";
                case "enum":
                    string note = Convert.ToString(param["note"], CultureInfo.InvariantCulture);
                    if (!string.IsNullOrEmpty(note))
                    {
                        string first = note.Split('|')[0].Trim().Split(' ')[0];
                        if (first.Length > 0) return first;
                    }
                    return "value";
                default:
                    if (name.IndexOf("component", StringComparison.OrdinalIgnoreCase) >= 0) return "Transform";
                    if (name.IndexOf("tag", StringComparison.OrdinalIgnoreCase) >= 0) return "Untagged";
                    if (name.IndexOf("layer", StringComparison.OrdinalIgnoreCase) >= 0) return 0;
                    if (name.IndexOf("menu", StringComparison.OrdinalIgnoreCase) >= 0) return "File/Save Project";
                    if (name.IndexOf("summary", StringComparison.OrdinalIgnoreCase) >= 0) return "why this runs";
                    if (name.IndexOf("type", StringComparison.OrdinalIgnoreCase) >= 0) return "Transform";
                    if (name.IndexOf("target", StringComparison.OrdinalIgnoreCase) >= 0) return "Canvas/Button";
                    if (name.IndexOf("name", StringComparison.OrdinalIgnoreCase) >= 0) return "NewName";
                    return "string";
            }
        }
    }
}
