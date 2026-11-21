using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Codely.UnityMcp
{
    /// <summary>
    /// Game View resolution control. Unity keeps this API internal, so it is driven
    /// through reflection and degrades to a clear not_supported error instead of throwing
    /// opaque NullReferenceExceptions when an editor version changes the shape.
    /// </summary>
    public static class ManageGameViewTool
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic |
                                         BindingFlags.Instance | BindingFlags.Static;

        public static ToolResult Handle(IDictionary<string, object> p)
        {
            string action = (JsonUtil.Str(p, "action", "get_resolution") ?? "get_resolution").Trim().ToLowerInvariant();
            switch (action)
            {
                case "get_resolution": return GetResolution();
                case "list_resolutions": return ListResolutions();
                case "set_resolution": return SetResolution(p);
                default:
                    throw new ToolException("unknown_action",
                        $"Unknown manage_gameview action '{action}'. Known actions: get_resolution, list_resolutions, set_resolution.");
            }
        }

        private static ToolResult GetResolution()
        {
            try
            {
                var group = GetGroup();
                int selected = GetSelectedIndex();
                int count = TotalCount(group);
                if (selected >= 0 && selected < count)
                {
                    var size = GetSize(group, selected);
                    if (size != null)
                        return new ToolResult("Game View resolution read.", new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            ["width"] = size.Value.Width,
                            ["height"] = size.Value.Height,
                            ["label"] = size.Value.Label,
                            ["index"] = selected,
                            ["approximate"] = false
                        });
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Codely Unity MCP: Game View reflection failed: " + ex.Message);
            }

            return new ToolResult("Game View reflection unavailable; returning screen size.", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["width"] = Screen.width,
                ["height"] = Screen.height,
                ["approximate"] = true,
                ["note"] = "GameView internals were not accessible in this editor version."
            });
        }

        private static ToolResult ListResolutions()
        {
            var group = GetGroup();
            int count = TotalCount(group);
            var items = new List<object>();

            for (int i = 0; i < count; i++)
            {
                var size = GetSize(group, i);
                if (size == null) continue;
                items.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["index"] = i,
                    ["width"] = size.Value.Width,
                    ["height"] = size.Value.Height,
                    ["label"] = size.Value.Label
                });
            }

            return new ToolResult($"Listed {items.Count} resolution(s).", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["count"] = items.Count,
                ["group"] = GroupIndex(),
                ["resolutions"] = items,
                ["note"] = "Indices are group-local. Use set_resolution with width/height instead of an index."
            });
        }

        private static ToolResult SetResolution(IDictionary<string, object> p)
        {
            int width = JsonUtil.Int(p, "width", 0);
            int height = JsonUtil.Int(p, "height", 0);
            if (width < 1 || height < 1 || width > 8192 || height > 8192)
                throw new ToolException("invalid_resolution", "width and height must be between 1 and 8192.");

            var group = GetGroup();
            int count = TotalCount(group);
            int index = -1;
            for (int i = 0; i < count; i++)
            {
                var size = GetSize(group, i);
                if (size.HasValue && size.Value.Width == width && size.Value.Height == height) { index = i; break; }
            }

            bool created = false;
            if (index < 0)
            {
                index = AddCustomSize(group, width, height);
                created = true;
            }

            if (index < 0)
                throw new ToolException("gameview_set_failed",
                    $"Could not select or create {width}x{height}. Call list_resolutions to see available sizes.");

            SetSelectedIndex(index);
            return new ToolResult($"Game View resolution set to {width}x{height}.", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["width"] = width,
                ["height"] = height,
                ["index"] = index,
                ["created"] = created
            });
        }

        // ---- reflection plumbing -------------------------------------------- //

        private static Type ResolveType(string name)
        {
            var type = typeof(EditorWindow).Assembly.GetType(name);
            if (type == null)
                throw new ToolException("gameview_unsupported", $"'{name}' was not found in this editor version.");
            return type;
        }

        private static object GetGroup()
        {
            var sizesType = ResolveType("UnityEditor.GameViewSizes");

            // This editor build exposes the active group directly; older ones only had
            // GameViewSizes.instance, so both paths are kept. The getter itself can throw
            // when no Game View has been opened yet, so it is probed defensively.
            object current = null;
            try { current = sizesType.GetProperty("currentGroup", Any)?.GetValue(null); }
            catch { /* fall through */ }
            if (current != null) return current;

            object instance = null;
            try
            {
                var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
                instance = singleton.GetProperty("instance", Any)?.GetValue(null);
            }
            catch
            {
                /* fall through to the error below */
            }

            if (instance == null)
                throw new ToolException("gameview_unsupported", "GameViewSizes had no current group and no instance.");

            var groupType = ResolveType("UnityEditor.GameViewSizeGroupType");
            object standalone = Enum.Parse(groupType, "Standalone");
            var getGroup = sizesType.GetMethod("GetGroup", Any, null, new[] { groupType }, null);
            if (getGroup == null)
                throw new ToolException("gameview_unsupported", "GameViewSizes.GetGroup was not found.");

            var group = getGroup.Invoke(instance, new[] { standalone });
            if (group == null)
                throw new ToolException("gameview_unsupported", "GameViewSizes.GetGroup returned null.");
            return group;
        }

        private static int TotalCount(object group)
        {
            var method = group.GetType().GetMethod("GetTotalCount", Any);
            if (method != null) return Convert.ToInt32(method.Invoke(group, null));
            var property = group.GetType().GetProperty("totalCount", Any);
            return property != null ? Convert.ToInt32(property.GetValue(group)) : 0;
        }

        private static (int Width, int Height, string Label)? GetSize(object group, int index)
        {
            var method = group.GetType().GetMethod("GetGameViewSize", Any);
            if (method == null) return null;
            object size;
            try { size = method.Invoke(group, new object[] { index }); }
            catch { return null; }
            if (size == null) return null;

            var type = size.GetType();
            int width = Convert.ToInt32(type.GetProperty("width", Any)?.GetValue(size) ?? 0);
            int height = Convert.ToInt32(type.GetProperty("height", Any)?.GetValue(size) ?? 0);
            string label = type.GetProperty("baseText", Any)?.GetValue(size) as string;
            return (width, height, label);
        }

        private static int AddCustomSize(object group, int width, int height)
        {
            var add = group.GetType().GetMethod("AddCustomSize", Any);
            if (add == null) return -1;

            var sizeType = ResolveType("UnityEditor.GameViewSize");
            object size;
            try
            {
                size = Activator.CreateInstance(sizeType, new object[] { width, height });
            }
            catch
            {
                var sizeTypeEnum = ResolveType("UnityEditor.GameViewSizeType");
                object fixedResolution = Enum.Parse(sizeTypeEnum, "FixedResolution");
                size = Activator.CreateInstance(sizeType, new object[] { fixedResolution, width, height, $"{width}x{height}" });
            }

            add.Invoke(group, new[] { size });
            return TotalCount(group) - 1;
        }

        private static EditorWindow FindGameView()
        {
            var gameViewType = ResolveType("UnityEditor.GameView");
            return Resources.FindObjectsOfTypeAll(gameViewType).FirstOrDefault() as EditorWindow;
        }

        private static int GroupIndex()
        {
            try
            {
                var sizesType = ResolveType("UnityEditor.GameViewSizes");
                var property = sizesType.GetProperty("currentGroupType", Any);
                if (property != null) return Convert.ToInt32(property.GetValue(null));
            }
            catch
            {
                /* default to Standalone */
            }
            return 0;
        }

        // This editor stores one selected index per size-group type.
        private static int GetSelectedIndex()
        {
            var gameView = FindGameView();
            if (gameView == null) return -1;

            var field = gameView.GetType().GetField("m_SelectedSizes", Any);
            if (field?.GetValue(gameView) is int[] sizes && sizes.Length > 0)
            {
                int index = GroupIndex();
                return index >= 0 && index < sizes.Length ? sizes[index] : sizes[0];
            }

            // Older editors used a single int field.
            var single = gameView.GetType().GetField("m_SelectedSizeIndex", Any);
            return single != null ? Convert.ToInt32(single.GetValue(gameView)) : -1;
        }

        private static void SetSelectedIndex(int index)
        {
            var gameView = FindGameView();
            if (gameView == null)
            {
                try { gameView = EditorWindow.GetWindow(ResolveType("UnityEditor.GameView")); }
                catch { /* ignore */ }
            }
            if (gameView == null)
                throw new ToolException("gameview_window_missing", "No Game View window is available.");

            var field = gameView.GetType().GetField("m_SelectedSizes", Any);
            if (field?.GetValue(gameView) is int[] sizes && sizes.Length > 0)
            {
                int group = GroupIndex();
                int slot = group >= 0 && group < sizes.Length ? group : 0;
                sizes[slot] = index;
                field.SetValue(gameView, sizes);
                gameView.Repaint();
                return;
            }

            var single = gameView.GetType().GetField("m_SelectedSizeIndex", Any);
            if (single == null)
                throw new ToolException("gameview_unsupported", "No selected-size field was found on GameView.");

            single.SetValue(gameView, index);
            gameView.Repaint();
        }
    }
}
