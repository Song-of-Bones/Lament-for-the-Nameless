using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Codely.UnityMcp
{
    /// <summary>
    /// Asset database operations. Search returns compact path/type rows (an agent needs
    /// locations, not full objects); create/modify work through SerializedObject so they
    /// behave like inspector edits and support undo.
    /// </summary>
    public static class ManageAssetTool
    {
        public static ToolResult Handle(IDictionary<string, object> p)
        {
            string action = (JsonUtil.Str(p, "action") ?? string.Empty).Trim().ToLowerInvariant();
            switch (action)
            {
                case "search": return Search(p);
                case "get_info": return GetInfo(p);
                case "create": return Create(p);
                case "modify": return Modify(p);
                case "ensure_prefab_variant": return EnsurePrefabVariant(p);
                default:
                    throw new ToolException("unknown_action",
                        $"Unknown manage_asset action '{action}'. Known actions: search, get_info, create, modify, ensure_prefab_variant.");
            }
        }

        private static ToolResult Search(IDictionary<string, object> p)
        {
            string folder = PathUtil.AssetsPath(JsonUtil.Str(p, "path", "Assets"), "Assets");
            if (!AssetDatabase.IsValidFolder(folder))
                throw new ToolException("folder_not_found", $"Folder '{folder}' does not exist.");

            string pattern = JsonUtil.Str(p, "searchPattern", string.Empty) ?? string.Empty;
            string filterType = JsonUtil.Str(p, "filterType");
            string query = pattern;
            if (!string.IsNullOrEmpty(filterType))
                query = (query + " t:" + filterType).Trim();

            string[] guids = AssetDatabase.FindAssets(query, new[] { folder });
            var all = new List<string>(guids.Length);
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path)) all.Add(path);
            }
            all.Sort(StringComparer.OrdinalIgnoreCase);

            DateTime? after = ParseDate(JsonUtil.Str(p, "filterDateAfter"));
            if (after.HasValue)
            {
                all = all.Where(path =>
                {
                    var info = new FileInfo(PathUtil.Absolute(path));
                    return info.Exists && info.LastWriteTimeUtc > after.Value;
                }).ToList();
            }

            int pageSize = Mathf.Clamp(JsonUtil.Int(p, "pageSize", 50), 1, 200);
            int pageNumber = Mathf.Max(1, JsonUtil.Int(p, "pageNumber", 1));
            int skip = (pageNumber - 1) * pageSize;
            var page = all.Skip(skip).Take(pageSize).ToList();

            var items = new List<object>(page.Count);
            foreach (var path in page)
            {
                var type = AssetDatabase.GetMainAssetTypeAtPath(path);
                items.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["path"] = path,
                    ["name"] = Path.GetFileNameWithoutExtension(path),
                    ["type"] = type != null ? type.Name : "unknown"
                });
            }

            var data = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["folder"] = folder,
                ["query"] = query,
                ["total"] = all.Count,
                ["page"] = pageNumber,
                ["pageSize"] = pageSize,
                ["pageCount"] = (int)Math.Ceiling(all.Count / (double)pageSize),
                ["items"] = items
            };
            if (all.Count == 0)
                data["hint"] = "No assets matched. searchPattern is a name substring; drop filterType to widen the search.";

            return new ToolResult($"Found {all.Count} asset(s).", data);
        }

        private static ToolResult GetInfo(IDictionary<string, object> p)
        {
            string path = PathUtil.AssetsPath(JsonUtil.Str(p, "path"));
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset == null) throw new ToolException("asset_not_found", $"Asset '{path}' could not be loaded.");

            var file = new FileInfo(PathUtil.Absolute(path));
            var importer = AssetImporter.GetAtPath(path);
            var dependencies = AssetDatabase.GetDependencies(path, true);

            return new ToolResult("Asset info collected.", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["path"] = path,
                ["name"] = asset.name,
                ["type"] = asset.GetType().Name,
                ["guid"] = AssetDatabase.AssetPathToGUID(path),
                ["size_bytes"] = file.Exists ? file.Length : 0,
                ["last_modified_utc"] = file.Exists ? file.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture) : null,
                ["importer"] = importer != null ? importer.GetType().Name : null,
                ["dependencies"] = dependencies.Take(100).Cast<object>().ToList(),
                ["dependency_count"] = dependencies.Length
            });
        }

        private static ToolResult Create(IDictionary<string, object> p)
        {
            string path = PathUtil.EnsureAssetsContained(JsonUtil.Str(p, "path"));
            string typeName = JsonUtil.Str(p, "assetType");
            if (string.IsNullOrEmpty(typeName)) throw new ToolException("missing_param", "assetType is required.");
            if (File.Exists(PathUtil.Absolute(path)))
                throw new ToolException("asset_exists", $"'{path}' already exists. Use modify instead.");

            PathUtil.EnsureFolder(PathUtil.DirectoryOf(path));
            Object asset;

            if (string.Equals(typeName, "Material", StringComparison.OrdinalIgnoreCase))
            {
                var props = JsonUtil.Obj(p, "properties");
                string shaderName = JsonUtil.Str(props, "shader");
                var shader = !string.IsNullOrEmpty(shaderName) ? Shader.Find(shaderName) : Shader.Find("Standard");
                if (shader == null && !string.IsNullOrEmpty(shaderName))
                    throw new ToolException("shader_not_found", $"Shader '{shaderName}' was not found.");
                asset = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
            }
            else
            {
                var type = ManageGameObjectTool.ResolveType(typeName);
                if (type == null) throw new ToolException("type_not_found", $"Type '{typeName}' was not found.");
                if (!typeof(Object).IsAssignableFrom(type))
                    throw new ToolException("invalid_asset_type", $"'{typeName}' is not a Unity Object type.");
                asset = ScriptableObject.CreateInstance(type);
                asset.name = Path.GetFileNameWithoutExtension(path);
            }

            AssetDatabase.CreateAsset(asset, path);
            var props2 = JsonUtil.Obj(p, "properties");
            if (props2 != null && props2.Count > 0)
            {
                if (asset is Material createdMaterial)
                {
                    // Materials expose shader property names, not the generic serialized
                    // fields ApplySerializedProperties walks.
                    foreach (var kv in props2)
                        ApplyMaterialProperty(createdMaterial, kv.Key, kv.Value);
                }
                else
                {
                    try { ApplySerializedProperties(asset, props2); }
                    catch (ToolException) { throw; }
                    catch (Exception ex) { Debug.LogWarning("Codely Unity MCP: initial properties partially applied: " + ex.Message); }
                }
            }
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            return new ToolResult($"Created '{path}'.", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["path"] = path,
                ["type"] = asset.GetType().Name,
                ["created"] = true
            });
        }

        private static ToolResult Modify(IDictionary<string, object> p)
        {
            string path = PathUtil.AssetsPath(JsonUtil.Str(p, "path"));
            var props = JsonUtil.Obj(p, "properties");
            if (props == null || props.Count == 0)
                throw new ToolException("missing_param", "properties is required.");

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return ModifyMaterial(path, material, props);

            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset == null) throw new ToolException("asset_not_found", $"Asset '{path}' could not be loaded.");

            Undo.RecordObject(asset, "Modify " + asset.name);
            var applied = ApplySerializedProperties(asset, props);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            return new ToolResult($"Modified '{path}'.", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["path"] = path,
                ["modified"] = applied
            });
        }

        private static ToolResult ModifyMaterial(string path, Material material, IDictionary<string, object> props)
        {
            Undo.RecordObject(material, "Modify material " + material.name);
            var modified = new List<object>();
            var unchanged = new List<object>();
            var warnings = new List<object>();

            foreach (var kv in props)
            {
                string key = kv.Key;
                object value = kv.Value;
                if (ApplyMaterialProperty(material, key, value))
                    modified.Add(key);
                else
                    unchanged.Add(key);
            }

            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();

            var data = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["path"] = path,
                ["modified"] = modified,
                ["unchanged"] = unchanged,
                ["warnings"] = warnings
            };

            if (modified.Count == 0)
                throw new ToolException("no_changes",
                    "No material property values changed. Use get_info or the shader's property names; " +
                    "aliases color/metallic/smoothness/float/texture are also accepted.",
                    data);

            return new ToolResult($"Modified {modified.Count} material property(ies).", data);
        }

        private static bool ApplyMaterialProperty(Material material, string key, object value)
        {
            string lower = key.ToLowerInvariant();
            switch (lower)
            {
                case "color":
                    material.color = ManageGameObjectTool.ToColor(value);
                    return true;
                case "metallic":
                    material.SetFloat("_Metallic", JsonUtil.ToFloat(value));
                    return true;
                case "smoothness":
                    if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", JsonUtil.ToFloat(value));
                    else material.SetFloat("_Smoothness", JsonUtil.ToFloat(value));
                    return true;
                case "float":
                    return false;
                case "texture":
                    return false;
            }

            if (material.shader == null) return false;
            int index = material.shader.FindPropertyIndex(key);
            if (index < 0) return false;

            switch (material.shader.GetPropertyType(index))
            {
                case UnityEngine.Rendering.ShaderPropertyType.Float:
                case UnityEngine.Rendering.ShaderPropertyType.Range:
                    material.SetFloat(key, JsonUtil.ToFloat(value));
                    return true;
                case UnityEngine.Rendering.ShaderPropertyType.Int:
                    material.SetInt(key, (int)JsonUtil.ToFloat(value));
                    return true;
                case UnityEngine.Rendering.ShaderPropertyType.Color:
                    material.SetColor(key, ManageGameObjectTool.ToColor(value));
                    return true;
                case UnityEngine.Rendering.ShaderPropertyType.Vector:
                    var v = ManageGameObjectTool.ToVector(value, 4);
                    material.SetVector(key, new Vector4(v.x, v.y, v.z, v.w));
                    return true;
                case UnityEngine.Rendering.ShaderPropertyType.Texture:
                    string texturePath = value as string;
                    var texture = string.IsNullOrEmpty(texturePath)
                        ? null
                        : AssetDatabase.LoadAssetAtPath<Texture>(PathUtil.AssetsPath(texturePath));
                    if (texture == null) return false;
                    material.SetTexture(key, texture);
                    return true;
                default:
                    return false;
            }
        }

        private static List<object> ApplySerializedProperties(Object asset, IDictionary<string, object> props)
        {
            var applied = new List<object>();
            var so = new SerializedObject(asset);
            foreach (var kv in props)
            {
                var prop = so.FindProperty(kv.Key);
                if (prop == null)
                    throw new ToolException("property_not_found",
                        $"Property '{kv.Key}' not found on {asset.GetType().Name}. " +
                        "Use manage_gameobject.get_components on a scene instance to discover serialized names.");
                AssignSerialized(prop, kv.Value);
                applied.Add(kv.Key);
            }
            so.ApplyModifiedProperties();
            return applied;
        }

        private static void AssignSerialized(SerializedProperty prop, object value)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.ArraySize:
                    prop.intValue = value is double d ? (int)d : Convert.ToInt32(value, CultureInfo.InvariantCulture);
                    break;
                case SerializedPropertyType.Boolean:
                    prop.boolValue = value is bool b ? b : Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                    break;
                case SerializedPropertyType.Float:
                    prop.floatValue = JsonUtil.ToFloat(value);
                    break;
                case SerializedPropertyType.String:
                    prop.stringValue = Convert.ToString(value, CultureInfo.InvariantCulture);
                    break;
                case SerializedPropertyType.Color:
                    prop.colorValue = ManageGameObjectTool.ToColor(value);
                    break;
                case SerializedPropertyType.Vector2:
                    var v2 = ManageGameObjectTool.ToVector(value, 2);
                    prop.vector2Value = new Vector2(v2.x, v2.y);
                    break;
                case SerializedPropertyType.Vector3:
                    prop.vector3Value = ManageGameObjectTool.ToVector(value, 3);
                    break;
                case SerializedPropertyType.Vector4:
                    var v4 = ManageGameObjectTool.ToVector(value, 4);
                    prop.vector4Value = new Vector4(v4.x, v4.y, v4.z, v4.w);
                    break;
                case SerializedPropertyType.Enum:
                    if (value is string enumName)
                    {
                        int index = Array.IndexOf(prop.enumNames, enumName);
                        if (index < 0)
                            throw new ToolException("invalid_enum", $"'{enumName}' is not one of: {string.Join(", ", prop.enumNames)}.");
                        prop.enumValueIndex = index;
                    }
                    else prop.enumValueIndex = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                    break;
                case SerializedPropertyType.ObjectReference:
                    string assetPath = value as string;
                    prop.objectReferenceValue = string.IsNullOrEmpty(assetPath)
                        ? null
                        : AssetDatabase.LoadAssetAtPath<Object>(PathUtil.AssetsPath(assetPath));
                    break;
                default:
                    throw new ToolException("unsupported_property_type",
                        $"Setting '{prop.name}' ({prop.propertyType}) is not supported.");
            }
        }

        private static ToolResult EnsurePrefabVariant(IDictionary<string, object> p)
        {
            string path = PathUtil.EnsureAssetsContained(JsonUtil.Str(p, "path"));
            string sourcePath = PathUtil.AssetsPath(JsonUtil.Str(p, "sourcePrefab"));
            if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                throw new ToolException("invalid_path", "path must end with .prefab.");
            if (File.Exists(PathUtil.Absolute(path)))
                return new ToolResult("Prefab variant already exists.", new Dictionary<string, object>(StringComparer.Ordinal)
                { ["path"] = path, ["created"] = false });

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null) throw new ToolException("source_not_found", $"Source prefab '{sourcePath}' was not found.");

            PathUtil.EnsureFolder(PathUtil.DirectoryOf(path));
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                PrefabUtility.SaveAsPrefabAsset(instance, path);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
            AssetDatabase.Refresh();

            return new ToolResult($"Created prefab variant '{path}'.", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["path"] = path,
                ["source"] = sourcePath,
                ["created"] = true
            });
        }

        private static DateTime? ParseDate(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            return DateTime.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed
                : (DateTime?)null;
        }
    }
}
