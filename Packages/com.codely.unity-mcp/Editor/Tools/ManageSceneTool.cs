using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Codely.UnityMcp
{
    /// <summary>
    /// Scene files and the loaded hierarchy. The hierarchy is the map an agent needs
    /// before touching objects, so it is capped by depth and node count instead of
    /// flooding the response on large scenes.
    /// </summary>
    public static class ManageSceneTool
    {
        public static ToolResult Handle(IDictionary<string, object> p)
        {
            string action = (JsonUtil.Str(p, "action", "get_hierarchy") ?? "get_hierarchy").Trim().ToLowerInvariant();
            switch (action)
            {
                case "load": return Load(p);
                case "save": return Save(p);
                case "create": return Create(p);
                case "get_hierarchy": return Hierarchy(p);
                default:
                    throw new ToolException("unknown_action",
                        $"Unknown manage_scene action '{action}'. Known actions: load, save, create, get_hierarchy.");
            }
        }

        private static ToolResult Load(IDictionary<string, object> p)
        {
            string resolved = ResolveScenePath(p, out string how);
            if (string.IsNullOrEmpty(resolved))
                throw new ToolException("scene_not_found",
                    "Could not resolve a scene. Provide an explicit path (Assets/Scenes/Main.unity), a unique name, or buildIndex.");

            var active = SceneManager.GetActiveScene();
            bool savedPrevious = false;
            if (active.isDirty)
            {
                if (!EditorSceneManager.SaveScene(active))
                    throw new ToolException("scene_save_failed",
                        "The current scene has unsaved changes and could not be saved. Save it explicitly before loading another scene.");
                savedPrevious = true;
            }

            if (string.Equals(active.path, resolved, StringComparison.OrdinalIgnoreCase))
                return new ToolResult("Scene already open.", new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["scene"] = resolved,
                    ["alreadyOpen"] = true,
                    ["savedPrevious"] = savedPrevious
                });

            var scene = EditorSceneManager.OpenScene(resolved, OpenSceneMode.Single);
            if (!scene.IsValid())
                throw new ToolException("scene_load_failed", $"Unity refused to open '{resolved}'. Check that the file is a valid scene.");

            return new ToolResult($"Opened scene '{resolved}'.", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["scene"] = resolved,
                ["name"] = scene.name,
                ["rootCount"] = scene.rootCount,
                ["resolvedBy"] = how,
                ["savedPrevious"] = savedPrevious
            });
        }

        private static ToolResult Save(IDictionary<string, object> p)
        {
            var active = SceneManager.GetActiveScene();
            if (!active.IsValid())
                throw new ToolException("no_scene", "No active scene to save.");

            string name = JsonUtil.Str(p, "name");
            string path = JsonUtil.Str(p, "path");
            bool saveAs = !string.IsNullOrEmpty(name) || !string.IsNullOrEmpty(path);

            if (!saveAs)
            {
                bool ok = EditorSceneManager.SaveScene(active);
                if (!ok) throw new ToolException("scene_save_failed", $"Failed to save '{active.path}'.");
                return new ToolResult("Scene saved.", new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["scene"] = active.path,
                    ["saved"] = true
                });
            }

            string folder = PathUtil.EnsureAssetsContained(string.IsNullOrEmpty(path) ? "Assets/Scenes" : path, "Assets/Scenes");
            string fileName = string.IsNullOrEmpty(name) ? active.name : name;
            if (!fileName.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) &&
                !fileName.EndsWith(".scene", StringComparison.OrdinalIgnoreCase))
                fileName += ".unity";

            PathUtil.EnsureFolder(folder);
            string target = folder + "/" + fileName;

            bool saved = EditorSceneManager.SaveScene(active, target);
            if (!saved) throw new ToolException("scene_save_failed", $"Failed to save scene to '{target}'.");
            AssetDatabase.Refresh();

            return new ToolResult($"Scene saved as '{target}'.", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["scene"] = target,
                ["saved"] = true,
                ["saveAs"] = true
            });
        }

        private static ToolResult Create(IDictionary<string, object> p)
        {
            string name = JsonUtil.Str(p, "name");
            if (string.IsNullOrWhiteSpace(name))
                throw new ToolException("missing_param", "name is required.");

            string folder = PathUtil.EnsureAssetsContained(JsonUtil.Str(p, "path", "Assets/Scenes"), "Assets/Scenes");
            string fileName = name.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ? name : name + ".unity";
            string target = folder + "/" + fileName;

            if (File.Exists(PathUtil.Absolute(target)))
                throw new ToolException("scene_exists", $"Scene '{target}' already exists. Use manage_scene.load instead.");

            var active = SceneManager.GetActiveScene();
            if (active.isDirty && !EditorSceneManager.SaveScene(active))
                throw new ToolException("scene_save_failed", "Current scene has unsaved changes and could not be saved.");

            PathUtil.EnsureFolder(folder);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            bool saved = EditorSceneManager.SaveScene(scene, target);
            if (!saved) throw new ToolException("scene_save_failed", $"Failed to create scene at '{target}'.");
            AssetDatabase.Refresh();

            return new ToolResult($"Created scene '{target}'.", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["scene"] = target,
                ["created"] = true
            });
        }

        private static ToolResult Hierarchy(IDictionary<string, object> p)
        {
            int maxDepth = Mathf.Clamp(JsonUtil.Int(p, "maxDepth", 6), 1, 32);
            int maxNodes = Mathf.Clamp(JsonUtil.Int(p, "maxNodes", 2000), 1, 20000);

            var scenes = new List<object>();
            int emitted = 0;
            bool truncated = false;

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid()) continue;

                var roots = new List<object>();
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (emitted >= maxNodes) { truncated = true; break; }
                    roots.Add(Describe(root.transform, 1, maxDepth, maxNodes, ref emitted, ref truncated));
                }

                scenes.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["name"] = scene.name,
                    ["path"] = scene.path,
                    ["isDirty"] = scene.isDirty,
                    ["rootCount"] = scene.rootCount,
                    ["roots"] = roots
                });

                if (emitted >= maxNodes) { truncated = true; break; }
            }

            var data = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["sceneCount"] = SceneManager.sceneCount,
                ["nodeCount"] = emitted,
                ["truncated"] = truncated,
                ["maxDepth"] = maxDepth,
                ["key_objects"] = KeyObjects(),
                ["scenes"] = scenes,
                ["next"] = "Use key_objects[].ref.path for camera/UI/manager targets, or manage_gameobject.find for anything else."
            };
            if (truncated)
                data["hint"] = "Hierarchy was truncated. Increase maxNodes/maxDepth, or use manage_gameobject.find/list_children to drill into a subtree.";

            return new ToolResult("Hierarchy collected.", data);
        }

        private static Dictionary<string, object> Describe(Transform t, int depth, int maxDepth, int maxNodes,
            ref int emitted, ref bool truncated)
        {
            emitted++;
            var components = new List<object>();
            foreach (var component in t.GetComponents<Component>())
            {
                if (component == null) { components.Add("<missing>"); continue; }
                components.Add(component.GetType().Name);
            }

            var node = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["name"] = t.name,
                ["instanceID"] = UnityEngine.EntityId.ToULong(t.gameObject.GetEntityId()),
                ["role"] = AiContract.RoleOf(t.gameObject),
                ["active"] = t.gameObject.activeSelf,
                ["tag"] = t.tag,
                ["layer"] = t.gameObject.layer,
                ["components"] = components
            };

            string role = Convert.ToString(node["role"]);
            bool interesting = role == "canvas" || role == "camera" || role == "event_system" ||
                               role == "manager" || role == "light" || role == "audio_listener";
            if (depth == 1 || interesting)
                node["ref"] = AiContract.RefFor(t.gameObject);

            if (depth < maxDepth)
            {
                var children = new List<object>();
                for (int i = 0; i < t.childCount; i++)
                {
                    if (emitted >= maxNodes) { truncated = true; break; }
                    children.Add(Describe(t.GetChild(i), depth + 1, maxDepth, maxNodes, ref emitted, ref truncated));
                }
                if (children.Count > 0) node["children"] = children;
            }
            else if (t.childCount > 0)
            {
                node["childrenOmitted"] = t.childCount;
                truncated = true;
            }

            return node;
        }

        /// <summary>
        /// Entry points an agent usually needs first: cameras, UI roots, event systems and
        /// manager-like objects, each with a ready-to-pass ref.
        /// </summary>
        private static List<object> KeyObjects()
        {
            var result = new List<object>();
            var seen = new HashSet<ulong>();

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid()) continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var component in root.GetComponentsInChildren<Component>(true))
                    {
                        if (component == null) continue;
                        string role = AiContract.RoleOf(component.gameObject);
                        if (role != "canvas" && role != "camera" && role != "event_system" &&
                            role != "manager" && role != "audio_listener")
                            continue;

                        ulong id = UnityEngine.EntityId.ToULong(component.gameObject.GetEntityId());
                        if (!seen.Add(id)) continue;

                        result.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            ["role"] = role,
                            ["name"] = component.gameObject.name,
                            ["path"] = ManageGameObjectTool.HierarchyPath(component.transform),
                            ["ref"] = AiContract.RefFor(component.gameObject)
                        });
                        if (result.Count >= 20) return result;
                    }
                }
            }
            return result;
        }

        // ---- resolution ------------------------------------------------------ //

        private static string ResolveScenePath(IDictionary<string, object> p, out string how)
        {
            how = null;

            int buildIndex = JsonUtil.Int(p, "buildIndex", -1, "build_index");
            if (buildIndex >= 0)
            {
                var build = EditorBuildSettings.scenes;
                if (buildIndex >= build.Length)
                    throw new ToolException("build_index_out_of_range",
                        $"buildIndex {buildIndex} is out of range (0-{Math.Max(0, build.Length - 1)}).");
                how = "buildIndex";
                return build[buildIndex].path;
            }

            string rawPath = JsonUtil.Str(p, "path");
            string name = JsonUtil.Str(p, "name");
            string extension = null;
            if (!string.IsNullOrEmpty(name))
            {
                if (name.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)) { extension = ".unity"; name = name.Substring(0, name.Length - 6); }
                else if (name.EndsWith(".scene", StringComparison.OrdinalIgnoreCase)) { extension = ".scene"; name = name.Substring(0, name.Length - 6); }
            }

            if (!string.IsNullOrEmpty(rawPath))
            {
                string normalized = PathUtil.AssetsPath(rawPath);
                bool looksLikeFile = normalized.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ||
                                     normalized.EndsWith(".scene", StringComparison.OrdinalIgnoreCase);
                if (looksLikeFile)
                {
                    if (!File.Exists(PathUtil.Absolute(normalized)))
                        throw new ToolException("scene_not_found", $"Scene file '{normalized}' does not exist.");
                    how = "path";
                    return normalized;
                }

                if (string.IsNullOrEmpty(name))
                    throw new ToolException("missing_param", "path is a directory; provide name as well.");

                string candidate = normalized + "/" + name + (extension ?? ".unity");
                if (File.Exists(PathUtil.Absolute(candidate)))
                {
                    how = "path+name";
                    return candidate;
                }

                string sceneExt = normalized + "/" + name + ".scene";
                if (File.Exists(PathUtil.Absolute(sceneExt)))
                {
                    how = "path+name";
                    return sceneExt;
                }

                throw new ToolException("scene_not_found", $"Scene '{candidate}' does not exist.");
            }

            if (string.IsNullOrEmpty(name))
            {
                var active = SceneManager.GetActiveScene();
                return active.IsValid() ? active.path : null;
            }

            var matches = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => string.Equals(Path.GetFileNameWithoutExtension(path), name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 1)
            {
                how = "name";
                return matches[0];
            }
            if (matches.Count > 1)
                throw new ToolException("ambiguous_scene",
                    $"Multiple scenes are named '{name}': {string.Join(", ", matches)}. Pass an explicit path.",
                    new Dictionary<string, object>(StringComparer.Ordinal) { ["matches"] = new List<object>(matches) });

            throw new ToolException("scene_not_found",
                $"No scene named '{name}' under Assets/. Use manage_asset.search with filterType 'Scene' to find candidates.");
        }
    }
}
