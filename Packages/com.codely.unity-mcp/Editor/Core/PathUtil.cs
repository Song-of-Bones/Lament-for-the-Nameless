using System;
using System.IO;
using UnityEditor;

namespace Codely.UnityMcp
{
    /// <summary>
    /// Path normalization and containment. Tools accept human-friendly paths
    /// ("Scenes/Main.unity", "Assets/Scenes/Main.unity", or an absolute path inside the
    /// project) but every write must land under Assets/ so an agent cannot escape the
    /// project by accident.
    /// </summary>
    public static class PathUtil
    {
        public static string ProjectRoot =>
            Directory.GetParent(UnityEngine.Application.dataPath)?.FullName ?? UnityEngine.Application.dataPath;

        public static string AssetsPath(string raw, string defaultFolder = "Assets")
        {
            if (string.IsNullOrWhiteSpace(raw)) return defaultFolder;

            string path = raw.Trim().Replace('\\', '/');
            string projectRoot = ProjectRoot.Replace('\\', '/');

            if (path.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
                path = path.Substring(projectRoot.Length + 1);

            if (path.StartsWith("/", StringComparison.Ordinal)) path = path.TrimStart('/');

            if (!path.StartsWith("Assets", StringComparison.OrdinalIgnoreCase) &&
                !path.StartsWith("Packages", StringComparison.OrdinalIgnoreCase) &&
                !path.StartsWith("ProjectSettings", StringComparison.OrdinalIgnoreCase))
            {
                path = defaultFolder.TrimEnd('/') + "/" + path.TrimStart('/');
            }

            path = path.Replace("//", "/");
            return path.TrimEnd('/');
        }

        public static string EnsureAssetsContained(string raw, string defaultFolder = "Assets")
        {
            string path = AssetsPath(raw, defaultFolder);
            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(path, "Assets", StringComparison.OrdinalIgnoreCase))
                throw new ToolException("path_outside_assets",
                    $"Path '{raw}' resolves outside Assets/ ('{path}'). Writes must stay under Assets/.");
            return path;
        }

        public static string Absolute(string assetPath)
            => Path.Combine(ProjectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));

        public static bool Exists(string assetPath) => AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null;

        public static void EnsureFolder(string assetFolder)
        {
            string folder = AssetsPath(assetFolder);
            if (string.IsNullOrEmpty(folder) || folder == "Assets") return;

            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        public static string DirectoryOf(string assetPath)
        {
            string normalized = AssetsPath(assetPath);
            int index = normalized.LastIndexOf('/');
            return index <= 0 ? "Assets" : normalized.Substring(0, index);
        }

        public static string FileNameWithoutExtension(string assetPath)
            => Path.GetFileNameWithoutExtension(assetPath.Replace('/', Path.DirectorySeparatorChar));

        public static string SanitizeFileName(string name, string fallback = "capture")
        {
            if (string.IsNullOrWhiteSpace(name)) return fallback;
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            name = name.Trim();
            return string.IsNullOrEmpty(name) ? fallback : name;
        }
    }
}
