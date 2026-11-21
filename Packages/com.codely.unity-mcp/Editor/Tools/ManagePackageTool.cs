using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Codely.UnityMcp
{
    /// <summary>
    /// UPM package operations. Install/remove can trigger a domain reload, so they use
    /// the same start-then-poll contract as compile/play.
    /// </summary>
    public static class ManagePackageTool
    {
        public static ToolResult Handle(IDictionary<string, object> p)
        {
            string action = (JsonUtil.Str(p, "action") ?? string.Empty).Trim().ToLowerInvariant();
            switch (action)
            {
                case "list_packages": return List();
                case "install_package": return Install(p);
                case "remove_package": return Remove(p);
                default:
                    throw new ToolException("unknown_action",
                        $"Unknown manage_package action '{action}'. Known actions: list_packages, install_package, remove_package.");
            }
        }

        private static ToolResult List()
        {
            var packages = new List<object>();
            try
            {
                foreach (var pkg in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                             .OrderBy(x => x.name, StringComparer.OrdinalIgnoreCase))
                {
                    if (pkg.name.StartsWith("com.unity.modules.", StringComparison.Ordinal)) continue;
                    packages.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["name"] = pkg.name,
                        ["displayName"] = pkg.displayName,
                        ["version"] = pkg.version,
                        ["source"] = pkg.source.ToString()
                    });
                }
            }
            catch (Exception ex)
            {
                throw new ToolException("package_list_failed", "Package enumeration failed: " + ex.Message);
            }

            return new ToolResult($"Listed {packages.Count} package(s).", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["count"] = packages.Count,
                ["packages"] = packages
            });
        }

        private static ToolResult Install(IDictionary<string, object> p)
        {
            string id = JsonUtil.Str(p, "id_or_url", null, "id", "name");
            if (string.IsNullOrWhiteSpace(id))
                throw new ToolException("missing_param", "id_or_url is required (package id, name, or git URL).");

            string version = JsonUtil.Str(p, "version");
            string identifier = string.IsNullOrEmpty(version) ? id : id + "@" + version;

            EditorOperation.Begin("package");
            EditorApplication.delayCall += () =>
            {
                try
                {
                    var request = Client.Add(identifier);
                    if (request == null)
                        Debug.LogError("Codely Unity MCP: Client.Add returned null for " + identifier);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Codely Unity MCP: package install failed for '{identifier}': {ex.Message}");
                }
            };

            return new ToolResult($"Install requested for '{identifier}'.", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["status"] = "started",
                ["identifier"] = identifier,
                ["next"] = "Poll manage_editor.get_operation; when done, call list_packages to confirm the resolved version."
            });
        }

        private static ToolResult Remove(IDictionary<string, object> p)
        {
            string name = JsonUtil.Str(p, "package_name", null, "name");
            if (string.IsNullOrWhiteSpace(name))
                throw new ToolException("missing_param", "package_name is required.");

            EditorOperation.Begin("package");
            EditorApplication.delayCall += () =>
            {
                try { Client.Remove(name); }
                catch (Exception ex)
                {
                    Debug.LogError($"Codely Unity MCP: package removal failed for '{name}': {ex.Message}");
                }
            };

            return new ToolResult($"Remove requested for '{name}'.", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["status"] = "started",
                ["package_name"] = name,
                ["next"] = "Poll manage_editor.get_operation; when done, call list_packages to confirm."
            });
        }
    }
}
