using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Codely.UnityMcp
{
    /// <summary>
    /// Shapes every tool response for machine consumption: stable entity references the
    /// agent can pass back, field schemas that say how a value may be changed, and a
    /// per-response meta block that answers "where am I and can I write right now".
    ///
    /// The goal is that an agent never needs to read project source to interpret a
    /// response or to build the next call.
    /// </summary>
    public static class AiContract
    {
        // ---- meta / blockers ------------------------------------------------- //

        public static Dictionary<string, object> Meta(string tool, string action)
        {
            var scene = SceneManager.GetActiveScene();
            var blockers = Blockers();

            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["tool"] = tool,
                ["action"] = action,
                ["project_root"] = McpBridgeServer.ProjectRoot,
                ["scene"] = scene.IsValid() ? scene.path : null,
                ["play_mode"] = EditorApplication.isPlaying
                    ? (EditorApplication.isPaused ? "paused" : "playing")
                    : (EditorApplication.isPlayingOrWillChangePlaymode ? "transitioning" : "stopped"),
                ["can_write"] = blockers.Count == 0,
                ["blockers"] = blockers,
                ["bridge"] = "codely-unity-mcp/" + McpBridgeServer.PackageVersion
            };
        }

        /// <summary>Reasons a project-mutating call would be rejected right now.</summary>
        public static List<object> Blockers()
        {
            var blockers = new List<object>();

            if (EditorApplication.isPlaying)
                blockers.Add(Blocker("play_mode", "The editor is in Play Mode; project edits would be discarded.",
                    "Call manage_editor.stop, then retry."));
            else if (EditorApplication.isPlayingOrWillChangePlaymode)
                blockers.Add(Blocker("play_mode_transition", "The editor is entering or leaving Play Mode.",
                    "Wait for the transition to finish (manage_editor.get_operation)."));

            if (EditorApplication.isCompiling)
                blockers.Add(Blocker("compiling", "Scripts are compiling; Unity may reload the domain.",
                    "Poll manage_editor.get_operation until state=done."));

            if (EditorApplication.isUpdating)
                blockers.Add(Blocker("importing", "Asset import or refresh is running.",
                    "Poll manage_editor.get_operation until state=done."));

            return blockers;
        }

        private static Dictionary<string, object> Blocker(string code, string reason, string fix)
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["code"] = code,
                ["reason"] = reason,
                ["fix"] = fix
            };
        }

        // ---- entity references ----------------------------------------------- //

        public static Dictionary<string, object> RefFor(GameObject go)
        {
            if (go == null) return null;
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["kind"] = "gameobject",
                ["name"] = go.name,
                ["path"] = ManageGameObjectTool.HierarchyPath(go.transform),
                ["instance_id"] = UnityEngine.EntityId.ToULong(go.GetEntityId()),
                ["scene"] = go.scene.IsValid() ? go.scene.path : null,
                ["pass_as"] = "target"
            };
        }

        public static Dictionary<string, object> RefFor(Component component)
        {
            if (component == null) return null;
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["kind"] = "component",
                ["type"] = component.GetType().Name,
                ["gameobject_path"] = ManageGameObjectTool.HierarchyPath(component.transform),
                ["instance_id"] = UnityEngine.EntityId.ToULong(component.GetEntityId()),
                ["pass_as"] = "componentName (+ target)"
            };
        }

        public static Dictionary<string, object> RefForAsset(string assetPath, Type type = null)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["kind"] = "asset",
                ["path"] = assetPath,
                ["guid"] = AssetDatabase.AssetPathToGUID(assetPath),
                ["type"] = type != null ? type.Name : DescribeAssetType(assetPath),
                ["pass_as"] = "path"
            };
        }

        private static string DescribeAssetType(string path)
        {
            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            return type != null ? type.Name : null;
        }

        /// <summary>
        /// Accepts a ref object returned by an earlier call and resolves the parts a tool
        /// needs. Lets agents chain calls without re-deriving names or paths.
        /// </summary>
        public static bool TryReadRef(object raw, out string path, out string id, out string kind)
        {
            path = null;
            id = null;
            kind = null;

            if (!(raw is IDictionary<string, object> dict)) return false;

            kind = JsonUtil.Str(dict, "kind");
            path = JsonUtil.Str(dict, "path") ?? JsonUtil.Str(dict, "gameobject_path");
            object rawId = JsonUtil.Raw(dict, "instance_id", "instanceID");
            if (rawId != null)
                id = Convert.ToString(rawId, System.Globalization.CultureInfo.InvariantCulture);

            return kind == "gameobject" || kind == "component" || kind == "asset";
        }

        // ---- field schemas ---------------------------------------------------- //

        /// <summary>
        /// Property name to value shape, so an agent knows the type and exactly which tool
        /// writes it. Values themselves stay in the sibling `properties` object.
        /// </summary>
        public static Dictionary<string, object> FieldSchema(Component component, bool includeHidden)
        {
            var schema = new Dictionary<string, object>(StringComparer.Ordinal);
            if (component == null) return schema;

            var so = new SerializedObject(component);
            var prop = so.GetIterator();
            bool enterChildren = true;
            int guard = 0;

            while (prop.NextVisible(enterChildren) && guard++ < 128)
            {
                enterChildren = false;
                if (prop.name == "m_Script") continue;
                if (!includeHidden && prop.name.StartsWith("m_", StringComparison.Ordinal)) continue;

                schema[prop.name] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["type"] = TypeName(prop),
                    ["settable"] = IsSettable(prop.propertyType),
                    ["setter"] = IsSettable(prop.propertyType) ? "manage_gameobject.set_component_property" : null,
                    ["enum_values"] = prop.propertyType == SerializedPropertyType.Enum
                        ? new List<object>(prop.enumNames.Cast<object>())
                        : null
                };
            }
            return schema;
        }

        public static string TypeName(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer: return "int";
                case SerializedPropertyType.Boolean: return "bool";
                case SerializedPropertyType.Float: return "float";
                case SerializedPropertyType.String: return "string";
                case SerializedPropertyType.Color: return "color";
                case SerializedPropertyType.ObjectReference: return "object_ref";
                case SerializedPropertyType.LayerMask: return "layer_mask";
                case SerializedPropertyType.Enum: return "enum";
                case SerializedPropertyType.Vector2: return "vec2";
                case SerializedPropertyType.Vector3: return "vec3";
                case SerializedPropertyType.Vector4: return "vec4";
                case SerializedPropertyType.Quaternion: return "quat";
                case SerializedPropertyType.Rect: return "rect";
                case SerializedPropertyType.ArraySize: return "array_size";
                case SerializedPropertyType.AnimationCurve: return "animation_curve";
                case SerializedPropertyType.Bounds: return "bounds";
                case SerializedPropertyType.Generic: return prop.isArray ? "array" : "struct";
                default: return prop.propertyType.ToString().ToLowerInvariant();
            }
        }

        public static bool IsSettable(SerializedPropertyType type)
        {
            switch (type)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.Boolean:
                case SerializedPropertyType.Float:
                case SerializedPropertyType.String:
                case SerializedPropertyType.Color:
                case SerializedPropertyType.ObjectReference:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Enum:
                case SerializedPropertyType.Vector2:
                case SerializedPropertyType.Vector3:
                case SerializedPropertyType.Vector4:
                    return true;
                default:
                    return false;
            }
        }

        // ---- semantic roles --------------------------------------------------- //

        /// <summary>Short role label so an agent can find the camera/UI root without guessing names.</summary>
        public static string RoleOf(GameObject go)
        {
            if (go == null) return "other";
            if (go.GetComponent<Canvas>() != null) return "canvas";
            if (go.GetComponent<Camera>() != null) return "camera";
            if (go.GetComponent<EventSystem>() != null) return "event_system";
            if (go.GetComponent<AudioListener>() != null) return "audio_listener";
            if (go.GetComponent<CanvasGroup>() != null || go.GetComponent<RectTransform>() != null) return "ui";
            if (go.GetComponent<Light>() != null) return "light";

            string name = go.name;
            if (Contains(name, "Manager") || Contains(name, "Controller") || Contains(name, "System") ||
                Contains(name, "Root") || Contains(name, "Bootstrap") || Contains(name, "App"))
                return "manager";

            return go.transform.childCount > 0 ? "group" : "object";
        }

        private static bool Contains(string text, string token)
            => text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

        // ---- console diagnosis ------------------------------------------------ //

        private static readonly (string Token, string Cause, string Fix)[] Rules =
        {
            ("NullReferenceException",
                "A reference field is unassigned or points at a destroyed object.",
                "Find the object named in the stack, open get_components, and check fields with type object_ref for null."),
            ("MissingReferenceException",
                "The script holds a reference to an object that was destroyed.",
                "Re-acquire the reference or guard with a null check; check the object's owning script."),
            ("The referenced script on this Behaviour is missing",
                "A component's script asset is missing or its class name no longer matches the file name.",
                "Locate the component with get_components and reassign or remove it; ensure the class name equals the file name."),
            ("does not have a definition for",
                "The code calls an API that does not exist in this Unity version.",
                "Inspect the real API with inspect_editor_api or adjust to the version's method name."),
            ("is not allowed to be called from a MonoBehaviour constructor",
                "A Unity API was called from a constructor or field initializer.",
                "Move the call into Awake/Start/OnEnable."),
            ("Can't add component",
                "The component conflicts with an existing one (for example two AudioListeners or a required dependency).",
                "Check existing components on the object before adding."),
            ("Shader", "A shader or material failed to compile; the renderer may show magenta.",
                "Inspect the material asset and its shader; check read_console with types ['error']."),
            ("Addressables", "Addressables content is not built or the key is missing.",
                "Rebuild Addressables content, or load the asset directly by path."),
            ("error CS", "A C# compile error blocks all script execution.",
                "Read the file/line in the message, fix the script, then manage_editor.request_compile.")
        };

        public static List<object> Diagnose(IEnumerable<string> messages)
        {
            var results = new List<object>();
            if (messages == null) return results;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var message in messages)
            {
                if (string.IsNullOrEmpty(message)) continue;
                foreach (var rule in Rules)
                {
                    if (message.IndexOf(rule.Token, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (!seen.Add(rule.Token)) continue;
                    results.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["matched"] = rule.Token,
                        ["likely_cause"] = rule.Cause,
                        ["fix"] = rule.Fix
                    });
                }
            }
            return results;
        }

        // ---- misc ------------------------------------------------------------- //

        public static Dictionary<string, object> Next(string tool, string action, string why)
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["tool"] = tool,
                ["action"] = action,
                ["why"] = why
            };
        }

        public static List<object> EmptyList() => new List<object>();
    }
}
