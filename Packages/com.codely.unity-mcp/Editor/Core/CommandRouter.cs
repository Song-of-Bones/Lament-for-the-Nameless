using System;
using System.Collections.Generic;

namespace Codely.UnityMcp
{
    public sealed class ToolResult
    {
        public string Message;
        public object Data;

        public ToolResult(string message, object data)
        {
            Message = message;
            Data = data;
        }
    }

    /// <summary>
    /// Single dispatch table for every bridge command. Keeping it in one place makes the
    /// wire surface obvious, and the unknown-type error points the caller at list_tools
    /// instead of leaving an agent guessing.
    /// </summary>
    public static class CommandRouter
    {
        public static readonly string[] ToolTypes =
        {
            "list_tools",
            "project_overview",
            "manage_editor",
            "manage_scene",
            "manage_gameobject",
            "manage_asset",
            "read_console",
            "execute_menu_item",
            "manage_package",
            "manage_gameview",
            "manage_guid",
            "manage_screenshot",
            "exec_editor_script"
        };

        public static object Execute(string type, IDictionary<string, object> p, out string message)
        {
            ToolResult result;
            switch (type)
            {
                case "list_tools":
                    result = ToolCatalog.Describe();
                    break;
                case "project_overview":
                    result = ProjectOverviewTool.Handle(p);
                    break;
                case "manage_editor":
                    result = ManageEditorTool.Handle(p);
                    break;
                case "manage_scene":
                    result = ManageSceneTool.Handle(p);
                    break;
                case "manage_gameobject":
                    result = ManageGameObjectTool.Handle(p);
                    break;
                case "manage_asset":
                    result = ManageAssetTool.Handle(p);
                    break;
                case "read_console":
                    result = ReadConsoleTool.Handle(p);
                    break;
                case "execute_menu_item":
                    result = ExecuteMenuItemTool.Handle(p);
                    break;
                case "manage_package":
                    result = ManagePackageTool.Handle(p);
                    break;
                case "manage_gameview":
                    result = ManageGameViewTool.Handle(p);
                    break;
                case "manage_guid":
                    result = ManageGuidTool.Handle(p);
                    break;
                case "manage_screenshot":
                    result = ManageScreenshotTool.Handle(p);
                    break;
                case "exec_editor_script":
                    result = ExecuteScriptTool.Handle(p);
                    break;
                default:
                    throw new ToolException(
                        "unknown_tool_type",
                        $"Unknown tool type '{type}'. Call list_tools to get the authoritative list. " +
                        $"Known types: {string.Join(", ", ToolTypes)}.",
                        new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            ["known_types"] = new List<object>(ToolTypes),
                            ["hint"] = "The first call in a new session should usually be project_overview."
                        });
            }

            message = result.Message;
            return result.Data;
        }
    }
}
