using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Codely.UnityMcp
{
    public sealed class ScriptField
    {
        public string Name;
        public string Type;
        public bool Serialized;
    }

    public sealed class ScriptMethod
    {
        public string Name;
        public string Returns;
        public string Params;
    }

    public sealed class ScriptInfo
    {
        public string Path;
        public string TypeName;
        public string BaseType;
        public string Namespace;
        public string Summary;
        public bool IsMonoBehaviour;
        public bool IsScriptableObject;
        public bool IsEditorScript;
        public List<ScriptField> Fields = new List<ScriptField>();
        public List<ScriptMethod> Methods = new List<ScriptMethod>();
        public List<string> Lifecycle = new List<string>();
    }

    /// <summary>
    /// Lightweight structural reading of C# scripts. Deliberately regex-based: it must
    /// work while the domain is reloading and must never take a dependency on a compiler
    /// assembly. The result is enough for an agent to answer "what does this script expose
    /// and where is it used" without opening the file.
    /// </summary>
    public static class ScriptAnalysis
    {
        private static readonly Regex NamespaceRx = new Regex(@"\bnamespace\s+([\w\.]+)", RegexOptions.Compiled);
        private static readonly Regex ClassRx = new Regex(@"\b(?:class|struct)\s+(\w+)\s*(?::\s*([\w\.<>]+))?", RegexOptions.Compiled);
        private static readonly Regex FieldRx = new Regex(
            @"^\s*(?:public|protected)\s+(?:static\s+)?(?:readonly\s+)?([\w\.<>,\[\]\?]+)\s+(\w+)\s*(?:=|;)",
            RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex SerializedFieldRx = new Regex(
            @"\[SerializeField\][^;\r\n]*?([\w\.<>,\[\]\?]+)\s+(\w+)\s*(?:=|;)",
            RegexOptions.Compiled);
        private static readonly Regex MethodRx = new Regex(
            @"^\s*(?:public|protected)\s+(?:static\s+)?(?:async\s+)?(?:override\s+)?(?:virtual\s+)?([\w\.<>,\[\]\?]+)\s+(\w+)\s*\(([^)]*)\)",
            RegexOptions.Compiled | RegexOptions.Multiline);

        // Unity lifecycle messages are usually private, so they are scanned separately
        // instead of relying on the public/protected method list.
        private static readonly Regex LifecycleRx = new Regex(
            @"\b(?:private|protected|public|internal)?\s*(?:static\s+)?(?:async\s+)?(?:void|IEnumerator|IEnumerator<[^>]+>)\s+(Awake|Start|OnEnable|OnDisable|Update|LateUpdate|FixedUpdate|OnDestroy|OnGUI|OnValidate|Reset)\s*\(",
            RegexOptions.Compiled);

        private static readonly string[] LifecycleNames =
        {
            "Awake", "Start", "OnEnable", "OnDisable", "Update", "LateUpdate",
            "FixedUpdate", "OnDestroy", "OnGUI", "OnValidate", "Reset"
        };

        public static ScriptInfo Analyze(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            string absolute = PathUtil.Absolute(assetPath);
            if (!File.Exists(absolute)) return null;

            var info = new ScriptInfo { Path = assetPath };
            var mono = AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath);
            var type = mono != null ? mono.GetClass() : null;

            string text = ReadHead(absolute, 128 * 1024);
            if (type != null)
            {
                info.TypeName = type.Name;
                info.BaseType = type.BaseType != null ? type.BaseType.Name : null;
                info.Namespace = type.Namespace;
                info.IsMonoBehaviour = typeof(MonoBehaviour).IsAssignableFrom(type);
                info.IsScriptableObject = typeof(ScriptableObject).IsAssignableFrom(type);
            }
            else
            {
                var classMatch = ClassRx.Match(text);
                if (classMatch.Success)
                {
                    info.TypeName = classMatch.Groups[1].Value;
                    info.BaseType = classMatch.Groups[2].Success ? classMatch.Groups[2].Value : null;
                }
                var nsMatch = NamespaceRx.Match(text);
                if (nsMatch.Success) info.Namespace = nsMatch.Groups[1].Value;
            }

            info.IsEditorScript = assetPath.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   info.BaseType == "Editor" || info.BaseType == "EditorWindow";

            var classDeclaration = ClassRx.Match(text);
            info.Summary = ExtractSummary(text, classDeclaration.Success ? classDeclaration.Index : 0);

            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match match in FieldRx.Matches(text))
            {
                string name = match.Groups[2].Value;
                if (!fields.Add(name)) continue;
                info.Fields.Add(new ScriptField
                {
                    Name = name,
                    Type = match.Groups[1].Value,
                    Serialized = true
                });
            }
            foreach (Match match in SerializedFieldRx.Matches(text))
            {
                string name = match.Groups[2].Value;
                if (!fields.Add(name)) continue;
                info.Fields.Add(new ScriptField
                {
                    Name = name,
                    Type = match.Groups[1].Value,
                    Serialized = true
                });
            }

            foreach (Match match in MethodRx.Matches(text))
            {
                string name = match.Groups[2].Value;
                if (name == info.TypeName) continue;
                info.Methods.Add(new ScriptMethod
                {
                    Name = name,
                    Returns = match.Groups[1].Value,
                    Params = CollapseWhitespace(match.Groups[3].Value)
                });
            }

            foreach (Match match in LifecycleRx.Matches(text))
            {
                string name = match.Groups[1].Value;
                if (!info.Lifecycle.Contains(name)) info.Lifecycle.Add(name);
            }
            info.Lifecycle = LifecycleNames.Where(n => info.Lifecycle.Contains(n)).ToList();

            return info;
        }

        private static Dictionary<string, string> _typePathCache;

        public static string FindScriptPathByType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;

            // Exact file name first: the common Unity convention.
            var byFileName = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => Path.GetFileNameWithoutExtension(path) == typeName)
                .ToList();
            if (byFileName.Count > 0) return byFileName[0];

            // This project renames script files to Chinese while class names stay English,
            // so file-name heuristics cannot find them. Resolve through the compiled type.
            var cache = TypePathCache();
            if (cache.TryGetValue(typeName, out string resolved)) return resolved;

            // Last resort: partial file-name match (English-named scripts).
            var matches = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => Path.GetFileNameWithoutExtension(path)
                    .IndexOf(typeName, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            return matches.Count > 0 ? matches[0] : null;
        }

        private static Dictionary<string, string> TypePathCache()
        {
            if (_typePathCache != null) return _typePathCache;

            var cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var guid in AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;

                var mono = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                var type = mono != null ? mono.GetClass() : null;
                if (type == null) continue;

                if (!cache.ContainsKey(type.Name)) cache[type.Name] = path;
                if (!string.IsNullOrEmpty(type.FullName) && !cache.ContainsKey(type.FullName))
                    cache[type.FullName] = path;
            }

            _typePathCache = cache;
            return cache;
        }

        /// <summary>Scene objects and prefabs that carry this component type.</summary>
        public static void CollectUsage(Type type, int maxPrefabs, out List<object> sceneObjects, out List<object> prefabs)
        {
            sceneObjects = new List<object>();
            prefabs = new List<object>();
            if (type == null) return;

            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.IsValid()) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var component in root.GetComponentsInChildren(type, true))
                    {
                        if (component == null) continue;
                        sceneObjects.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            ["scene"] = scene.path,
                            ["path"] = ManageGameObjectTool.HierarchyPath(component.transform),
                            ["instance_id"] = UnityEngine.EntityId.ToULong(component.gameObject.GetEntityId())
                        });
                        if (sceneObjects.Count >= 200) return;
                    }
                }
            }

            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
            int inspected = 0;
            foreach (var guid in prefabGuids)
            {
                if (inspected++ >= maxPrefabs) break;
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                if (prefab.GetComponentsInChildren(type, true).Length == 0) continue;
                prefabs.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["path"] = path,
                    ["name"] = prefab.name
                });
                if (prefabs.Count >= 100) break;
            }
        }

        public static string RoleOf(ScriptInfo info)
        {
            if (info == null) return "other";
            string name = info.TypeName ?? string.Empty;

            if (info.IsScriptableObject) return "data_asset";
            if (info.IsEditorScript) return "editor_tool";
            if (Contains(name, "Manager") || Contains(name, "Controller") || Contains(name, "System") ||
                Contains(name, "Bootstrap") || Contains(name, "App")) return "manager";
            if (Contains(name, "UI") || Contains(name, "View") || Contains(name, "Panel") ||
                Contains(name, "Window") || Contains(name, "Button") || Contains(name, "Popup")) return "ui";
            if (Contains(name, "Data") || Contains(name, "Config") || Contains(name, "Settings") ||
                Contains(name, "State")) return "data";
            return "behaviour";
        }

        private static bool Contains(string text, string token)
            => text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

        private static string ExtractSummary(string text, int beforeIndex)
        {
            if (string.IsNullOrEmpty(text) || beforeIndex <= 0) return null;

            var lines = text.Substring(0, Math.Min(beforeIndex, text.Length))
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToList();

            var comments = new List<string>();
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                string line = lines[i];
                if (line.StartsWith("///", StringComparison.Ordinal))
                    comments.Insert(0, line.Substring(3).Trim());
                else if (line.StartsWith("//", StringComparison.Ordinal))
                    comments.Insert(0, line.Substring(2).Trim());
                else if (line.StartsWith("[", StringComparison.Ordinal))
                    continue;
                else
                    break;
            }

            if (comments.Count == 0) return null;
            string summary = string.Join(" ", comments.Where(c => !c.StartsWith("<", StringComparison.Ordinal)));
            return summary.Length > 240 ? summary.Substring(0, 240) : summary;
        }

        private static string ReadHead(string absolutePath, int maxBytes)
        {
            try
            {
                using (var stream = new FileStream(absolutePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    int length = (int)Math.Min(maxBytes, stream.Length);
                    var buffer = new byte[length];
                    int read = stream.Read(buffer, 0, length);
                    return System.Text.Encoding.UTF8.GetString(buffer, 0, read);
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string CollapseWhitespace(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return Regex.Replace(text, @"\s+", " ").Trim();
        }
    }
}
