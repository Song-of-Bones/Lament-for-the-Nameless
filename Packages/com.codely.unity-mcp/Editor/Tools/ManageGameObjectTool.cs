using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Codely.UnityMcp
{
    /// <summary>
    /// Scene object operations. Every mutation goes through Undo and marks the scene
    /// dirty so an agent's edits behave like a human's in the editor.
    /// </summary>
    public static class ManageGameObjectTool
    {
        public static ToolResult Handle(IDictionary<string, object> p)
        {
            string action = (JsonUtil.Str(p, "action") ?? string.Empty).Trim().ToLowerInvariant();
            switch (action)
            {
                case "find": return Find(p);
                case "create": return Create(p);
                case "modify": return Modify(p);
                case "delete": return Delete(p);
                case "get_components": return GetComponents(p);
                case "remove_component": return RemoveComponent(p);
                case "set_component_property": return SetComponentProperty(p);
                case "list_children": return ListChildren(p);
                case "ensure_component": return EnsureComponent(p);
                case "ensure_renderer_material": return EnsureRendererMaterial(p);
                case "ensure_mesh_collider_mesh": return EnsureMeshColliderMesh(p);
                case "ensure_prefab_default_sprite": return EnsurePrefabDefaultSprite(p);
                default:
                    throw new ToolException("unknown_action",
                        $"Unknown manage_gameobject action '{action}'. Known actions: find, create, modify, delete, " +
                        "get_components, remove_component, set_component_property, list_children, ensure_component, " +
                        "ensure_renderer_material, ensure_mesh_collider_mesh, ensure_prefab_default_sprite.");
            }
        }

        // ---- find ------------------------------------------------------------ //

        private static ToolResult Find(IDictionary<string, object> p)
        {
            var matches = Resolve(p, true);
            int limit = JsonUtil.Int(p, "limit", 100);
            if (limit <= 0) limit = 100;

            var items = matches.Take(limit).Select(Describe).ToList();
            var data = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["count"] = matches.Count,
                ["returned"] = items.Count,
                ["objects"] = items
            };
            if (matches.Count == 0)
                data["hint"] = "No match. Try searchTerm for a partial name, or manage_scene.get_hierarchy to inspect the whole scene.";
            if (matches.Count > items.Count)
                data["hint"] = "More matches exist; increase limit or narrow the search.";

            return new ToolResult(matches.Count == 0 ? "No matching objects." : $"Found {matches.Count} object(s).", data);
        }

        private static Dictionary<string, object> Describe(GameObject go)
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["name"] = go.name,
                ["path"] = HierarchyPath(go.transform),
                ["instanceID"] = UnityEngine.EntityId.ToULong(go.GetEntityId()),
                ["ref"] = AiContract.RefFor(go),
                ["role"] = AiContract.RoleOf(go),
                ["active"] = go.activeSelf,
                ["tag"] = go.tag,
                ["layer"] = go.layer,
                ["scene"] = go.scene.IsValid() ? go.scene.path : null,
                ["components"] = go.GetComponents<Component>().Where(c => c != null).Select(c => (object)c.GetType().Name).ToList()
            };
        }

        internal static List<GameObject> Resolve(IDictionary<string, object> p, bool allowMultiple)
        {
            string target = JsonUtil.Str(p, "target");
            string term = JsonUtil.Str(p, "searchTerm");
            string method = JsonUtil.Str(p, "searchMethod");
            bool includeInactive = JsonUtil.Bool(p, "searchInactive", true);
            bool findAll = JsonUtil.Bool(p, "findAll", false);

            if (string.IsNullOrEmpty(target) && string.IsNullOrEmpty(term))
            {
                // Agents can pass back a ref from a previous response instead of
                // re-deriving a name or hierarchy path.
                object refRaw = JsonUtil.Raw(p, "ref", "targetRef");
                if (refRaw != null && AiContract.TryReadRef(refRaw, out string refPath, out string refId, out _))
                    target = string.IsNullOrEmpty(refPath) ? refId : refPath;
            }

            if (string.IsNullOrEmpty(target) && string.IsNullOrEmpty(term))
                throw new ToolException("missing_param", "One of 'target', 'searchTerm' or 'ref' is required.");

            var candidates = Resources.FindObjectsOfTypeAll<GameObject>()
                .Where(go => go != null && !EditorUtility.IsPersistent(go) && go.scene.IsValid())
                .Where(go => includeInactive || go.activeInHierarchy)
                .ToList();

            List<GameObject> result;
            if (!string.IsNullOrEmpty(term))
            {
                result = candidates
                    .Where(go => go.name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(go => HierarchyPath(go.transform), StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            else
            {
                method = (method ?? string.Empty).ToLowerInvariant();
                if (method == "by_id" || (method.Length == 0 && ulong.TryParse(target, out _)))
                {
                    string idText = target;
                    result = candidates
                        .Where(go => UnityEngine.EntityId.ToULong(go.GetEntityId()).ToString() == idText)
                        .ToList();
                }
                else if (method == "by_path" || (method.Length == 0 && target.Contains("/")))
                {
                    result = candidates
                        .Where(go => string.Equals(HierarchyPath(go.transform), target, StringComparison.OrdinalIgnoreCase) ||
                                     HierarchyPath(go.transform).EndsWith("/" + target, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }
                else
                {
                    result = candidates
                        .Where(go => string.Equals(go.name, target, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (result.Count == 0)
                        result = candidates.Where(go => go.name.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                }
            }

            return findAll ? result : result.Take(1).ToList();
        }

        internal static GameObject RequireOne(IDictionary<string, object> p)
        {
            var matches = Resolve(p, true);
            if (matches.Count == 0)
                throw new ToolException("object_not_found",
                    "No matching GameObject. Use manage_gameobject.find or manage_scene.get_hierarchy first.");

            // Duplicate names are common in real scenes; operate on the first match but
            // tell the caller how to be precise next time instead of failing the call.
            return matches[0];
        }

        internal static string HierarchyPath(Transform t)
        {
            var parts = new List<string>();
            while (t != null)
            {
                parts.Insert(0, t.name);
                t = t.parent;
            }
            return string.Join("/", parts);
        }

        // ---- create ---------------------------------------------------------- //

        private static ToolResult Create(IDictionary<string, object> p)
        {
            string name = JsonUtil.Str(p, "name");
            if (string.IsNullOrWhiteSpace(name)) throw new ToolException("missing_param", "name is required.");

            string primitive = JsonUtil.Str(p, "primitiveType");
            GameObject go;
            if (!string.IsNullOrEmpty(primitive))
            {
                if (!Enum.TryParse(primitive, true, out PrimitiveType type))
                    throw new ToolException("invalid_primitive", $"Unknown primitiveType '{primitive}'. Use cube, sphere, capsule, cylinder, plane or quad.");
                go = GameObject.CreatePrimitive(type);
                go.name = name;
            }
            else
            {
                go = new GameObject(name);
            }

            Undo.RegisterCreatedObjectUndo(go, "Create " + name);

            string parentPath = JsonUtil.Str(p, "parent");
            if (!string.IsNullOrEmpty(parentPath))
                go.transform.SetParent(ResolveParent(parentPath), false);

            if (JsonUtil.TryVec3(p, "position", out Vector3 position)) go.transform.localPosition = position;
            if (JsonUtil.TryVec3(p, "rotation", out Vector3 rotation)) go.transform.localEulerAngles = rotation;
            if (JsonUtil.TryVec3(p, "scale", out Vector3 scale)) go.transform.localScale = scale;

            string tag = JsonUtil.Str(p, "tag");
            if (!string.IsNullOrEmpty(tag)) go.tag = tag;
            if (JsonUtil.Has(p, "layer")) go.layer = JsonUtil.Int(p, "layer", go.layer);

            var added = new List<object>();
            var components = JsonUtil.List(p, "componentsToAdd");
            foreach (var item in components)
            {
                var spec = item as IDictionary<string, object>;
                string typeName = JsonUtil.Str(spec, "type", JsonUtil.Str(spec, "component"));
                if (string.IsNullOrEmpty(typeName))
                    throw new ToolException("invalid_component", "componentsToAdd entries need a 'type'.");
                var component = AddComponent(go, typeName);
                added.Add(component.GetType().Name);
                ApplyProperties(component, JsonUtil.Obj(spec, "properties"));
            }

            bool saveAsPrefab = JsonUtil.Bool(p, "saveAsPrefab", false);
            string prefabPath = JsonUtil.Str(p, "prefabPath");
            if (saveAsPrefab)
            {
                if (string.IsNullOrEmpty(prefabPath))
                    prefabPath = $"Assets/Prefabs/{name}.prefab";
                prefabPath = PathUtil.EnsureAssetsContained(prefabPath, "Assets/Prefabs");
                if (!prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) prefabPath += ".prefab";
                PathUtil.EnsureFolder(PathUtil.DirectoryOf(prefabPath));
                PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            }

            MarkDirty(go);
            Selection.activeGameObject = go;

            return new ToolResult($"Created '{name}'.", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["name"] = name,
                ["path"] = HierarchyPath(go.transform),
                ["instanceID"] = UnityEngine.EntityId.ToULong(go.GetEntityId()),
                ["componentsAdded"] = added,
                ["prefabPath"] = saveAsPrefab ? prefabPath : null
            });
        }

        private static Transform ResolveParent(string parentPath)
        {
            var matches = Resources.FindObjectsOfTypeAll<GameObject>()
                .Where(go => go != null && !EditorUtility.IsPersistent(go) && go.scene.IsValid())
                .Where(go => string.Equals(HierarchyPath(go.transform), parentPath, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(go.name, parentPath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count == 0)
                throw new ToolException("parent_not_found", $"Parent '{parentPath}' not found.");
            return matches[0].transform;
        }

        // ---- modify ---------------------------------------------------------- //

        private static ToolResult Modify(IDictionary<string, object> p)
        {
            var go = RequireOne(p);
            Undo.RecordObject(go, "Modify " + go.name);
            Undo.RecordObject(go.transform, "Modify transform " + go.name);

            var changed = new List<object>();

            string name = JsonUtil.Str(p, "name");
            if (!string.IsNullOrEmpty(name)) { go.name = name; changed.Add("name"); }

            string tag = JsonUtil.Str(p, "tag");
            if (!string.IsNullOrEmpty(tag)) { go.tag = tag; changed.Add("tag"); }

            if (JsonUtil.Has(p, "layer")) { go.layer = JsonUtil.Int(p, "layer", go.layer); changed.Add("layer"); }
            if (JsonUtil.Has(p, "setActive")) { go.SetActive(JsonUtil.Bool(p, "setActive", go.activeSelf)); changed.Add("active"); }

            if (p.ContainsKey("parent"))
            {
                string parent = JsonUtil.Str(p, "parent");
                go.transform.SetParent(string.IsNullOrEmpty(parent) ? null : ResolveParent(parent), false);
                changed.Add("parent");
            }

            if (JsonUtil.TryVec3(p, "position", out Vector3 position)) { go.transform.localPosition = position; changed.Add("position"); }
            if (JsonUtil.TryVec3(p, "rotation", out Vector3 rotation)) { go.transform.localEulerAngles = rotation; changed.Add("rotation"); }
            if (JsonUtil.TryVec3(p, "scale", out Vector3 scale)) { go.transform.localScale = scale; changed.Add("scale"); }

            foreach (var item in JsonUtil.List(p, "componentsToAdd"))
            {
                var spec = item as IDictionary<string, object>;
                string typeName = JsonUtil.Str(spec, "type", JsonUtil.Str(spec, "component"));
                if (string.IsNullOrEmpty(typeName)) continue;
                var component = AddComponent(go, typeName);
                ApplyProperties(component, JsonUtil.Obj(spec, "properties"));
                changed.Add("+" + component.GetType().Name);
            }

            foreach (var item in JsonUtil.List(p, "componentsToRemove"))
            {
                string typeName = Convert.ToString(item, CultureInfo.InvariantCulture);
                var component = FindComponent(go, typeName);
                if (component == null) continue;
                Undo.DestroyObjectImmediate(component);
                changed.Add("-" + typeName);
            }

            var overrides = JsonUtil.Obj(p, "componentProperties");
            if (overrides != null)
            {
                foreach (var kv in overrides)
                {
                    var component = FindComponent(go, kv.Key);
                    if (component == null)
                        throw new ToolException("component_not_found", $"Component '{kv.Key}' is not on '{go.name}'.");
                    ApplyProperties(component, kv.Value as IDictionary<string, object>);
                    changed.Add(kv.Key);
                }
            }

            if (changed.Count == 0)
                throw new ToolException("no_changes", "No recognized fields were provided; nothing was modified.");

            MarkDirty(go);
            return new ToolResult($"Modified '{go.name}'.", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["name"] = go.name,
                ["path"] = HierarchyPath(go.transform),
                ["changed"] = changed
            });
        }

        private static ToolResult Delete(IDictionary<string, object> p)
        {
            var go = RequireOne(p);
            string path = HierarchyPath(go.transform);
            Undo.DestroyObjectImmediate(go);
            return new ToolResult($"Deleted '{path}'.", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["deleted"] = path
            });
        }

        // ---- components ------------------------------------------------------ //

        private static ToolResult GetComponents(IDictionary<string, object> p)
        {
            var go = RequireOne(p);
            bool includeHidden = JsonUtil.Bool(p, "includeNonPublicSerialized", true);
            var result = new List<object>();

            foreach (var component in go.GetComponents<Component>())
            {
                if (component == null) continue;
                var entry = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["type"] = component.GetType().Name,
                    ["fullType"] = component.GetType().FullName,
                    ["ref"] = AiContract.RefFor(component),
                    ["role"] = AiContract.RoleOf(component.gameObject)
                };
                if (component is Behaviour behaviour) entry["enabled"] = behaviour.enabled;
                entry["properties"] = SerializeProperties(component, includeHidden);
                entry["field_schema"] = AiContract.FieldSchema(component, includeHidden);
                result.Add(entry);
            }

            return new ToolResult($"Listed {result.Count} component(s).", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["name"] = go.name,
                ["path"] = HierarchyPath(go.transform),
                ["ref"] = AiContract.RefFor(go),
                ["components"] = result,
                ["next"] = "Set a value with manage_gameobject.set_component_property; field_schema[name].type tells you the JSON shape."
            });
        }

        private static Dictionary<string, object> SerializeProperties(Component component, bool includeHidden)
        {
            var properties = new Dictionary<string, object>(StringComparer.Ordinal);
            try
            {
                var so = new SerializedObject(component);
                var prop = so.GetIterator();
                bool enterChildren = true;
                int guard = 0;
                while (prop.NextVisible(enterChildren) && guard++ < 128)
                {
                    enterChildren = false;
                    if (prop.name == "m_Script") continue;
                    if (!includeHidden && prop.name.StartsWith("m_", StringComparison.Ordinal)) continue;
                    properties[prop.name] = PropertyValue(prop);
                }
            }
            catch (Exception ex)
            {
                properties["_error"] = ex.Message;
            }
            return properties;
        }

        private static object PropertyValue(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer: return prop.intValue;
                case SerializedPropertyType.Boolean: return prop.boolValue;
                case SerializedPropertyType.Float: return prop.floatValue;
                case SerializedPropertyType.String: return prop.stringValue;
                case SerializedPropertyType.Color:
                    var c = prop.colorValue;
                    return new List<object> { c.r, c.g, c.b, c.a };
                case SerializedPropertyType.Vector2:
                    var v2 = prop.vector2Value; return new List<object> { v2.x, v2.y };
                case SerializedPropertyType.Vector3:
                    var v3 = prop.vector3Value; return new List<object> { v3.x, v3.y, v3.z };
                case SerializedPropertyType.Vector4:
                    var v4 = prop.vector4Value; return new List<object> { v4.x, v4.y, v4.z, v4.w };
                case SerializedPropertyType.Quaternion:
                    var q = prop.quaternionValue; return new List<object> { q.x, q.y, q.z, q.w };
                case SerializedPropertyType.Enum: return prop.enumValueIndex;
                case SerializedPropertyType.ObjectReference:
                    return prop.objectReferenceValue != null
                        ? AssetDatabase.GetAssetPath(prop.objectReferenceValue)
                        : null;
                case SerializedPropertyType.LayerMask: return prop.intValue;
                case SerializedPropertyType.ArraySize: return prop.intValue;
                case SerializedPropertyType.Generic:
                    return prop.isArray ? $"array[{prop.arraySize}]" : "<struct>";
                default:
                    return "<" + prop.propertyType + ">";
            }
        }

        private static ToolResult RemoveComponent(IDictionary<string, object> p)
        {
            var go = RequireOne(p);
            var targets = new List<string>();
            string single = JsonUtil.Str(p, "componentName");
            if (!string.IsNullOrEmpty(single)) targets.Add(single);
            foreach (var item in JsonUtil.List(p, "componentsToRemove"))
                targets.Add(Convert.ToString(item, CultureInfo.InvariantCulture));

            if (targets.Count == 0)
                throw new ToolException("missing_param", "componentName or componentsToRemove is required.");

            var removed = new List<object>();
            foreach (var typeName in targets)
            {
                var component = FindComponent(go, typeName);
                if (component == null) continue;
                Undo.DestroyObjectImmediate(component);
                removed.Add(typeName);
            }

            if (removed.Count == 0)
                throw new ToolException("component_not_found", $"None of the requested components exist on '{go.name}'.");

            MarkDirty(go);
            return new ToolResult($"Removed {removed.Count} component(s).", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["name"] = go.name,
                ["removed"] = removed
            });
        }

        private static ToolResult SetComponentProperty(IDictionary<string, object> p)
        {
            var go = RequireOne(p);
            string componentName = JsonUtil.Str(p, "componentName");
            if (string.IsNullOrEmpty(componentName))
                throw new ToolException("missing_param", "componentName is required.");

            var component = FindComponent(go, componentName);
            if (component == null)
                throw new ToolException("component_not_found", $"'{go.name}' has no component named '{componentName}'.");

            var values = new Dictionary<string, object>(StringComparer.Ordinal);
            string propertyName = JsonUtil.Str(p, "propertyName");
            if (!string.IsNullOrEmpty(propertyName))
                values[propertyName] = JsonUtil.Raw(p, "propertyValue");

            var bulk = JsonUtil.Obj(p, "componentProperties");
            if (bulk != null)
                foreach (var kv in bulk) values[kv.Key] = kv.Value;

            if (values.Count == 0)
                throw new ToolException("missing_param", "Provide propertyName/propertyValue or componentProperties.");

            var applied = ApplyProperties(component, values);
            MarkDirty(go);

            return new ToolResult($"Updated {component.GetType().Name}.", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["name"] = go.name,
                ["component"] = component.GetType().Name,
                ["applied"] = applied
            });
        }

        private static List<object> ApplyProperties(Component component, IDictionary<string, object> values)
        {
            var applied = new List<object>();
            if (values == null || values.Count == 0) return applied;

            Undo.RecordObject(component, "Set " + component.GetType().Name);
            var so = new SerializedObject(component);
            foreach (var kv in values)
            {
                var prop = so.FindProperty(kv.Key);
                if (prop == null)
                    throw new ToolException("property_not_found",
                        $"Property '{kv.Key}' not found on {component.GetType().Name}. Use get_components to list property names.");
                AssignProperty(prop, kv.Value);
                applied.Add(kv.Key);
            }
            so.ApplyModifiedProperties();
            return applied;
        }

        private static void AssignProperty(SerializedProperty prop, object value)
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
                case SerializedPropertyType.Enum:
                    if (value is string enumName)
                    {
                        var names = prop.enumNames;
                        int index = Array.IndexOf(names, enumName);
                        if (index < 0) throw new ToolException("invalid_enum", $"'{enumName}' is not one of: {string.Join(", ", names)}.");
                        prop.enumValueIndex = index;
                    }
                    else
                    {
                        prop.enumValueIndex = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                    }
                    break;
                case SerializedPropertyType.Color:
                    prop.colorValue = ToColor(value);
                    break;
                case SerializedPropertyType.Vector2:
                    var v2 = ToVector(value, 2);
                    prop.vector2Value = new Vector2(v2.x, v2.y);
                    break;
                case SerializedPropertyType.Vector3:
                    prop.vector3Value = ToVector(value, 3);
                    break;
                case SerializedPropertyType.Vector4:
                    var v4 = ToVector(value, 4);
                    prop.vector4Value = new Vector4(v4.x, v4.y, v4.z, v4.w);
                    break;
                case SerializedPropertyType.ObjectReference:
                    string assetPath = value as string;
                    prop.objectReferenceValue = string.IsNullOrEmpty(assetPath)
                        ? null
                        : AssetDatabase.LoadAssetAtPath<Object>(PathUtil.AssetsPath(assetPath));
                    if (!string.IsNullOrEmpty(assetPath) && prop.objectReferenceValue == null)
                        throw new ToolException("asset_not_found", $"Asset '{assetPath}' could not be loaded.");
                    break;
                default:
                    throw new ToolException("unsupported_property_type",
                        $"Setting '{prop.name}' ({prop.propertyType}) is not supported by this bridge yet.");
            }
        }

        internal static Color ToColor(object value)
        {
            if (value is List<object> list)
            {
                float r = list.Count > 0 ? JsonUtil.ToFloat(list[0]) : 0f;
                float g = list.Count > 1 ? JsonUtil.ToFloat(list[1]) : 0f;
                float b = list.Count > 2 ? JsonUtil.ToFloat(list[2]) : 0f;
                float a = list.Count > 3 ? JsonUtil.ToFloat(list[3]) : 1f;
                return new Color(r, g, b, a);
            }
            if (value is IDictionary<string, object> dict)
                return new Color(JsonUtil.Float(dict, "r"), JsonUtil.Float(dict, "g"), JsonUtil.Float(dict, "b"), JsonUtil.Float(dict, "a", 1f));
            throw new ToolException("invalid_value", "Color expects [r,g,b,a] or {r,g,b,a}.");
        }

        internal static Vector4 ToVector(object value, int size)
        {
            if (value is List<object> list)
            {
                return new Vector4(
                    list.Count > 0 ? JsonUtil.ToFloat(list[0]) : 0f,
                    list.Count > 1 ? JsonUtil.ToFloat(list[1]) : 0f,
                    list.Count > 2 ? JsonUtil.ToFloat(list[2]) : 0f,
                    list.Count > 3 ? JsonUtil.ToFloat(list[3]) : 0f);
            }
            if (value is IDictionary<string, object> dict)
                return new Vector4(JsonUtil.Float(dict, "x"), JsonUtil.Float(dict, "y"), JsonUtil.Float(dict, "z"), JsonUtil.Float(dict, "w"));
            throw new ToolException("invalid_value", $"Vector{size} expects [x,y,z,w] or {{x,y,z,w}}.");
        }

        private static ToolResult ListChildren(IDictionary<string, object> p)
        {
            var go = RequireOne(p);
            var children = new List<object>();
            foreach (Transform child in go.transform)
            {
                children.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["name"] = child.name,
                    ["path"] = HierarchyPath(child),
                    ["instanceID"] = UnityEngine.EntityId.ToULong(child.gameObject.GetEntityId()),
                    ["active"] = child.gameObject.activeSelf,
                    ["childCount"] = child.childCount
                });
            }
            return new ToolResult($"{children.Count} child object(s).", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["parent"] = HierarchyPath(go.transform),
                ["count"] = children.Count,
                ["children"] = children
            });
        }

        // ---- ensure ---------------------------------------------------------- //

        private static ToolResult EnsureComponent(IDictionary<string, object> p)
        {
            var go = RequireOne(p);
            string typeName = JsonUtil.Str(p, "component_type", null, "componentType");
            if (string.IsNullOrEmpty(typeName)) throw new ToolException("missing_param", "component_type is required.");

            var existing = FindComponent(go, typeName);
            if (existing != null)
                return new ToolResult($"'{typeName}' already present.", new Dictionary<string, object>(StringComparer.Ordinal)
                { ["created"] = false, ["component"] = existing.GetType().Name });

            var added = AddComponent(go, typeName);
            MarkDirty(go);
            return new ToolResult($"Added '{added.GetType().Name}'.", new Dictionary<string, object>(StringComparer.Ordinal)
            { ["created"] = true, ["component"] = added.GetType().Name });
        }

        private static ToolResult EnsureRendererMaterial(IDictionary<string, object> p)
        {
            var go = RequireOne(p);
            string materialPath = JsonUtil.Str(p, "material");
            if (string.IsNullOrEmpty(materialPath)) throw new ToolException("missing_param", "material is required.");

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) throw new ToolException("renderer_missing", $"'{go.name}' has no Renderer.");

            if (renderer.sharedMaterial != null)
                return new ToolResult("Renderer already has a material.", new Dictionary<string, object>(StringComparer.Ordinal)
                { ["assigned"] = false, ["material"] = renderer.sharedMaterial.name });

            var material = FindAsset<Material>(materialPath);
            if (material == null) throw new ToolException("material_not_found", $"Material '{materialPath}' not found.");

            Undo.RecordObject(renderer, "Assign material");
            renderer.sharedMaterial = material;
            MarkDirty(go);

            return new ToolResult($"Assigned '{material.name}'.", new Dictionary<string, object>(StringComparer.Ordinal)
            { ["assigned"] = true, ["material"] = material.name });
        }

        private static ToolResult EnsureMeshColliderMesh(IDictionary<string, object> p)
        {
            var go = RequireOne(p);
            var collider = go.GetComponent<MeshCollider>();
            if (collider == null) throw new ToolException("collider_missing", $"'{go.name}' has no MeshCollider.");
            if (collider.sharedMesh != null)
                return new ToolResult("MeshCollider already has a mesh.", new Dictionary<string, object>(StringComparer.Ordinal)
                { ["assigned"] = false, ["mesh"] = collider.sharedMesh.name });

            string meshPath = JsonUtil.Str(p, "mesh");
            Mesh mesh = null;
            if (!string.IsNullOrEmpty(meshPath)) mesh = FindAsset<Mesh>(meshPath);
            if (mesh == null)
            {
                var filter = go.GetComponent<MeshFilter>();
                mesh = filter != null ? filter.sharedMesh : null;
            }
            if (mesh == null) throw new ToolException("mesh_not_found", "No mesh available from MeshFilter or the supplied path.");

            Undo.RecordObject(collider, "Assign mesh");
            collider.sharedMesh = mesh;
            MarkDirty(go);
            return new ToolResult($"Assigned mesh '{mesh.name}'.", new Dictionary<string, object>(StringComparer.Ordinal)
            { ["assigned"] = true, ["mesh"] = mesh.name });
        }

        private static ToolResult EnsurePrefabDefaultSprite(IDictionary<string, object> p)
        {
            string prefabPath = JsonUtil.Str(p, "prefab");
            if (string.IsNullOrEmpty(prefabPath)) throw new ToolException("missing_param", "prefab is required.");
            prefabPath = PathUtil.AssetsPath(prefabPath);

            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null) throw new ToolException("prefab_not_found", $"Prefab '{prefabPath}' could not be loaded.");

            try
            {
                var renderer = root.GetComponentInChildren<SpriteRenderer>(true);
                if (renderer == null)
                    throw new ToolException("sprite_renderer_missing", $"'{prefabPath}' has no SpriteRenderer.");
                if (renderer.sprite != null)
                    return new ToolResult("Sprite already assigned.", new Dictionary<string, object>(StringComparer.Ordinal)
                    { ["assigned"] = false, ["sprite"] = renderer.sprite.name });

                string spritePath = JsonUtil.Str(p, "sprite");
                Sprite sprite = !string.IsNullOrEmpty(spritePath) ? FindAsset<Sprite>(spritePath) : null;
                if (sprite == null)
                    throw new ToolException("sprite_not_found", "Provide a sprite path; no default sprite could be inferred.");

                renderer.sprite = sprite;
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                return new ToolResult($"Assigned sprite '{sprite.name}'.", new Dictionary<string, object>(StringComparer.Ordinal)
                { ["assigned"] = true, ["sprite"] = sprite.name });
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ---- helpers --------------------------------------------------------- //

        private static Component AddComponent(GameObject go, string typeName)
        {
            var type = ResolveType(typeName);
            if (type == null) throw new ToolException("type_not_found", $"Type '{typeName}' was not found in any loaded assembly.");
            if (!typeof(Component).IsAssignableFrom(type))
                throw new ToolException("not_a_component", $"'{typeName}' is not a Component.");

            var existing = go.GetComponent(type);
            if (existing != null) return existing;
            return Undo.AddComponent(go, type);
        }

        internal static Component FindComponent(GameObject go, string typeName)
        {
            var type = ResolveType(typeName);
            if (type != null) return go.GetComponent(type);
            return go.GetComponents<Component>()
                .FirstOrDefault(c => c != null && string.Equals(c.GetType().Name, typeName, StringComparison.OrdinalIgnoreCase));
        }

        internal static Type ResolveType(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            // UAC0005: scanning the live assembly set is intended here; SafeTypes swallows
            // reflection failures from assemblies that are being unloaded.
#pragma warning disable UAC0005
            var types = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(SafeTypes)
                .Where(t => t != null)
                .Where(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(t.FullName, name, StringComparison.OrdinalIgnoreCase))
                .ToList();
#pragma warning restore UAC0005

            return types.FirstOrDefault(t => t.Namespace != null && t.Namespace.StartsWith("UnityEngine", StringComparison.Ordinal))
                   ?? types.FirstOrDefault(t => t.Namespace != null && t.Namespace.StartsWith("UnityEditor", StringComparison.Ordinal))
                   ?? types.FirstOrDefault();
        }

        private static IEnumerable<Type> SafeTypes(System.Reflection.Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (System.Reflection.ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null); }
            catch { return Array.Empty<Type>(); }
        }

        internal static T FindAsset<T>(string query) where T : Object
        {
            string path = PathUtil.AssetsPath(query);
            var direct = AssetDatabase.LoadAssetAtPath<T>(path);
            if (direct != null) return direct;

            string fileName = System.IO.Path.GetFileNameWithoutExtension(query);
            var guid = AssetDatabase.FindAssets($"{fileName} t:{typeof(T).Name}", new[] { "Assets" }).FirstOrDefault();
            return string.IsNullOrEmpty(guid) ? null : AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
        }

        private static void MarkDirty(GameObject go)
        {
            if (go != null && go.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(go.scene);
        }
    }
}
