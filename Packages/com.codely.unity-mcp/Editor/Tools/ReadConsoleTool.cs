using System;
using System.Collections.Generic;

namespace Codely.UnityMcp
{
    public static class ReadConsoleTool
    {
        public static ToolResult Handle(IDictionary<string, object> p)
        {
            string action = (JsonUtil.Str(p, "action", "get") ?? "get").Trim().ToLowerInvariant();
            switch (action)
            {
                case "get":
                    var data = ConsoleBuffer.Read(p);
                    return new ToolResult($"Returned {data["count"]} console entrie(s).", data);
                case "clear":
                    return new ToolResult("Console buffer cleared.", ConsoleBuffer.Clear(p));
                case "set_collapse":
                    return new ToolResult("Console grouping preference saved.", ConsoleBuffer.SetGrouping(p));
                default:
                    throw new ToolException("unknown_action", $"Unknown read_console action '{action}'. Known actions: get, clear, set_collapse.");
            }
        }
    }

    public static class ManageGuidTool
    {
        public static ToolResult Handle(IDictionary<string, object> p)
        {
            string action = (JsonUtil.Str(p, "action", "path_to_guid") ?? "path_to_guid").Trim().ToLowerInvariant();
            if (action != "path_to_guid")
                throw new ToolException("unknown_action", $"Unknown manage_guid action '{action}'. Only path_to_guid is supported.");

            string assetPath = PathUtil.AssetsPath(JsonUtil.Str(p, "asset_path", JsonUtil.Str(p, "path")));
            string guid = UnityEditor.AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid))
                throw new ToolException("asset_not_found", $"No asset database entry for '{assetPath}'.");

            string expected = JsonUtil.Str(p, "guid");
            var data = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["asset_path"] = assetPath,
                ["guid"] = guid
            };

            if (!string.IsNullOrEmpty(expected))
            {
                bool matches = string.Equals(guid, expected, StringComparison.OrdinalIgnoreCase);
                data["expected"] = expected;
                data["matches"] = matches;
                if (!matches)
                    throw new ToolException("guid_mismatch",
                        $"Asset guid is '{guid}', not the expected '{expected}'.", data);
            }

            return new ToolResult("Guid resolved.", data);
        }
    }
}
