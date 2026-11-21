using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;

namespace Codely.UnityMcp
{
    /// <summary>
    /// Menu execution plus a best-effort menu listing. The listing scans [MenuItem]
    /// attributes once per domain, which gives agents real paths to call instead of a
    /// hand-written list that drifts.
    /// </summary>
    public static class ExecuteMenuItemTool
    {
        private static List<string> _cachedMenus;

        public static ToolResult Handle(IDictionary<string, object> p)
        {
            string action = (JsonUtil.Str(p, "action", "execute") ?? "execute").Trim().ToLowerInvariant();
            switch (action)
            {
                case "execute": return Execute(p);
                case "get_available_menus": return ListMenus(p);
                default:
                    throw new ToolException("unknown_action",
                        $"Unknown execute_menu_item action '{action}'. Known actions: execute, get_available_menus.");
            }
        }

        private static ToolResult Execute(IDictionary<string, object> p)
        {
            string path = JsonUtil.Str(p, "menu_path", null, "menuPath");
            if (string.IsNullOrWhiteSpace(path))
                throw new ToolException("missing_param", "menu_path is required, e.g. 'File/Save Project'.");

            bool ok;
            try { ok = EditorApplication.ExecuteMenuItem(path); }
            catch (Exception ex)
            {
                throw new ToolException("menu_failed", $"Menu '{path}' threw: {ex.Message}");
            }

            if (!ok)
                throw new ToolException("menu_not_found",
                    $"Menu item '{path}' does not exist or is disabled. Use get_available_menus to find valid paths.");

            return new ToolResult($"Executed menu '{path}'.", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["menu_path"] = path,
                ["executed"] = true
            });
        }

        private static ToolResult ListMenus(IDictionary<string, object> p)
        {
            string root = JsonUtil.Str(p, "menu_path", null, "menuPath");
            string filter = JsonUtil.Str(p, "filter");
            int limit = Math.Max(1, JsonUtil.Int(p, "limit", 500));

            var menus = AllMenus()
                .Where(m => string.IsNullOrEmpty(root) || m.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                .Where(m => string.IsNullOrEmpty(filter) || m.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .Take(limit)
                .Cast<object>()
                .ToList();

            return new ToolResult($"{menus.Count} menu path(s).", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["count"] = menus.Count,
                ["menus"] = menus,
                ["note"] = "Listing comes from [MenuItem] attributes; dynamic menus added at runtime may be missing."
            });
        }

        private static readonly string[] CommonMenus =
        {
            "File/Save Project", "File/Save As...", "File/Build Settings...", "File/Build And Run",
            "Edit/Play", "Edit/Pause", "Edit/Step", "Edit/Undo", "Edit/Redo", "Edit/Select All", "Edit/Deselect All",
            "Assets/Refresh", "Assets/Create/Folder", "Assets/Find References In Scene",
            "Window/General/Console", "Window/General/Hierarchy", "Window/General/Inspector",
            "Window/General/Project", "Window/General/Scene", "Window/General/Game",
            "Tools/Codely/Unity MCP"
        };

        private static List<string> AllMenus()
        {
            if (_cachedMenus != null) return _cachedMenus;

            // Reflection only sees menus declared with [MenuItem]; much of Unity's built-in
            // menu bar is constructed at runtime, so common paths are seeded explicitly.
            var menus = new HashSet<string>(CommonMenus, StringComparer.Ordinal);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
                catch { continue; }

                foreach (var type in types)
                {
                    try
                    {
                        foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                        {
                            var attr = method.GetCustomAttribute<MenuItem>();
                            if (attr == null || string.IsNullOrEmpty(attr.menuItem)) continue;
                            if (attr.menuItem.StartsWith("CONTEXT/", StringComparison.Ordinal)) continue;
                            menus.Add(attr.menuItem);
                        }
                    }
                    catch { /* ignore broken assemblies */ }
                }
            }

            _cachedMenus = menus.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
            return _cachedMenus;
        }
    }
}
