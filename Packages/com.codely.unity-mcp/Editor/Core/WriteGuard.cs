using System;
using System.Collections.Generic;
using UnityEditor;

namespace Codely.UnityMcp
{
    /// <summary>
    /// Central Play Mode write gate. Clients are stateless and cannot be trusted to
    /// remember that Unity discards scene/asset edits made during Play Mode, so the
    /// bridge refuses them here and returns an actionable message instead.
    ///
    /// Reads, screenshots, console access and Game View control stay available while
    /// playing; anything that mutates project state is blocked.
    /// </summary>
    public static class WriteGuard
    {
        private static readonly HashSet<string> AlwaysBlockedInPlay = new HashSet<string>(StringComparer.Ordinal)
        {
            "manage_package",
            "manage_bake"
        };

        private static readonly Dictionary<string, HashSet<string>> BlockedActions =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
            {
                ["manage_scene"] = new HashSet<string>(new[] { "load", "save", "create" }, StringComparer.Ordinal),
                ["manage_gameobject"] = new HashSet<string>(new[]
                {
                    "create", "modify", "delete", "remove_component", "set_component_property",
                    "ensure_component", "ensure_renderer_material", "ensure_mesh_collider_mesh",
                    "ensure_prefab_default_sprite"
                }, StringComparer.Ordinal),
                ["manage_asset"] = new HashSet<string>(new[]
                {
                    "create", "modify", "ensure_prefab_variant"
                }, StringComparer.Ordinal),
                ["manage_editor"] = new HashSet<string>(new[]
                {
                    "refresh", "request_compile", "start_compilation_pipeline",
                    "add_tag", "remove_tag", "ensure_tag",
                    "add_layer", "remove_layer", "ensure_layer"
                }, StringComparer.Ordinal),
                ["exec_editor_script"] = new HashSet<string>(new[] { "*" }, StringComparer.Ordinal)
            };

        public static Dictionary<string, object> Check(string type, IDictionary<string, object> p)
        {
            if (string.IsNullOrEmpty(type)) return null;
            if (!EditorApplication.isPlayingOrWillChangePlaymode) return null;

            string action = JsonUtil.Str(p, "action");
            bool blocked = AlwaysBlockedInPlay.Contains(type);

            if (!blocked && action == null && type == "exec_editor_script")
                blocked = true;

            if (!blocked && action != null && BlockedActions.TryGetValue(type, out var actions))
                blocked = actions.Contains(action) || actions.Contains("*");

            if (!blocked) return null;

            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["code"] = "blocked_in_play_mode",
                ["message"] =
                    $"'{type}{(action == null ? string.Empty : "." + action)}' changes project state and is blocked while the editor is in Play Mode. " +
                    "Call manage_editor.stop first (it discards runtime state), then retry. Read-only tools, screenshots and manage_gameview still work in Play Mode.",
                ["play_mode"] = EditorApplication.isPlaying ? "playing" : "transitioning",
                ["suggested_action"] = "manage_editor.stop",
                ["retryable"] = true
            };
        }
    }
}
