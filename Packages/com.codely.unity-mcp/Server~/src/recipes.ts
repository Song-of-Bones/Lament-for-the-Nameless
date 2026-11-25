/**
 * Verified task recipes. Each one is a concrete, ordered call sequence an agent can copy
 * instead of inventing a workflow — the main cure for "I understand the request but not
 * how to do it in Unity".
 *
 * Recipes intentionally include the verification step and the pitfall that most often
 * breaks that task.
 */
export interface RecipeStep {
  tool: string;
  action?: string;
  params?: Record<string, unknown>;
  why: string;
}

export interface Recipe {
  id: string;
  goal: string;
  when: string[];
  requires?: string[];
  steps: RecipeStep[];
  verify: string[];
  pitfalls: string[];
}

export const RECIPES: Recipe[] = [
  {
    id: "ui-change-text",
    goal: "Change the text displayed by a UI label",
    when: [
      "the user asks to change, replace or fix visible text",
      "a title, button caption, subtitle or hint needs new wording"
    ],
    requires: ["the scene that contains the UI is loaded"],
    steps: [
      {
        tool: "manage_scene",
        action: "get_hierarchy",
        params: { maxDepth: 4, maxNodes: 800 },
        why: "Find the UI root. key_objects lists canvas/camera/event_system so you do not guess names."
      },
      {
        tool: "manage_gameobject",
        action: "find",
        params: { searchTerm: "Text", searchInactive: true, findAll: true },
        why: "Narrow candidates by name. Use the exact path from objects[].path next time."
      },
      {
        tool: "manage_gameobject",
        action: "get_components",
        params: { target: "Canvas/Title" },
        why: "components[].type gives the exact text component (TextMeshProUGUI, Text, TMP_Text subclass) and field_schema confirms the string field."
      },
      {
        tool: "manage_gameobject",
        action: "set_component_property",
        params: { target: "Canvas/Title", componentName: "TextMeshProUGUI", propertyName: "m_text", propertyValue: "New text" },
        why: "m_text is the serialized string on TMP components; use the name reported by field_schema, which may differ per component."
      }
    ],
    verify: [
      "manage_gameobject.get_components again and read properties.m_text",
      "manage_screenshot capture_scene_view for a visual check (UI lives in the scene view only if a canvas is visible)"
    ],
    pitfalls: [
      "Do not assume the component is called Text; read components[].type.",
      "If the label is driven by a localization system, the field may be replaced at runtime — check inspect_script for the owner script."
    ]
  },
  {
    id: "swap-material-texture",
    goal: "Change what a 3D object or UI image looks like (material, texture, color)",
    when: ["user asks to recolor, reskin or swap an image/texture", "a renderer shows the wrong art"],
    steps: [
      {
        tool: "manage_asset",
        action: "search",
        params: { searchPattern: "button", filterType: "Texture2D", pageSize: 10 },
        why: "Resolve the exact asset path before assigning it."
      },
      {
        tool: "manage_gameobject",
        action: "find",
        params: { target: "Canvas/Button" },
        why: "Locate the exact renderer owner; ref.path from the result is reusable."
      },
      {
        tool: "manage_gameobject",
        action: "get_components",
        params: { target: "Canvas/Button" },
        why: "Confirm whether it is a SpriteRenderer (sprite), Image (sprite + color) or MeshRenderer (material)."
      },
      {
        tool: "manage_asset",
        action: "modify",
        params: { path: "Assets/Materials/Button.mat", properties: { _BaseColor: [0.2, 0.6, 1, 1] } },
        why: "Changing the shared material affects every user of it; use shader property names or the color/metallic/smoothness aliases."
      }
    ],
    verify: [
      "manage_screenshot capture_specific_camera or capture_scene_view",
      "read_console types ['error'] to catch shader or missing-reference errors"
    ],
    pitfalls: [
      "Material properties are shader-specific: run tool_help manage_asset modify, then use get_info/field names from the shader.",
      "Assigning a different material to one object needs exec_editor_script today (renderer.sharedMaterial = ...), because there is no dedicated action."
    ]
  },
  {
    id: "add-ui-button",
    goal: "Add a working UI button (visual + click handler)",
    when: ["user asks for a new button, menu entry or tappable element"],
    requires: ["a Canvas with an EventSystem exists in the scene"],
    steps: [
      {
        tool: "exec_editor_script",
        params: {
          summary: "create UI button",
          script:
            'var parent = GameObject.Find("Canvas").transform;\n' +
            'var go = new GameObject("McpButton", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));\n' +
            'go.transform.SetParent(parent, false);\n' +
            'var rt = (RectTransform)go.transform; rt.sizeDelta = new Vector2(240, 72); rt.anchoredPosition = Vector2.zero;\n' +
            'return go.GetComponent<RectTransform>().anchoredPosition;'
        },
        why: "A UI element needs RectTransform before Image/Button; creating it through one script avoids an invalid intermediate state."
      },
      {
        tool: "manage_gameobject",
        action: "set_component_property",
        params: { target: "Canvas/McpButton", componentName: "Image", propertyName: "m_Color", propertyValue: [0.1, 0.1, 0.1, 1] },
        why: "Set the visual style with the serialized color field reported by get_components."
      },
      {
        tool: "exec_editor_script",
        params: {
          summary: "bind button click",
          script:
            'var button = GameObject.Find("Canvas/McpButton").GetComponent<UnityEngine.UI.Button>();\n' +
            'var target = GameObject.Find("Canvas").GetComponent<MonoBehaviour>();\n' +
            'return button != null;'
        },
        why: "Persistent onClick listeners need UnityEventTools; a plain tool call cannot express them yet. Replace the second line with the real handler object."
      }
    ],
    verify: [
      "manage_gameobject.get_components target 'Canvas/McpButton' and confirm Image + Button",
      "manage_screenshot in Play Mode is limited; prefer a scene screenshot for layout"
    ],
    pitfalls: [
      "Without an EventSystem, clicks do nothing.",
      "A Button without a target Graphic is not clickable — keep the Image on the same object.",
      "Text children need a TMP component and a font asset; see recipe ui-change-text."
    ]
  },
  {
    id: "aim-camera",
    goal: "Point or move a camera and verify the framing",
    when: ["user asks to change the camera angle, zoom, FOV or position"],
    steps: [
      {
        tool: "manage_scene",
        action: "get_hierarchy",
        params: { maxDepth: 2, maxNodes: 200 },
        why: "key_objects lists cameras with a ready ref; avoids relying on Camera.main."
      },
      {
        tool: "manage_gameobject",
        action: "modify",
        params: { target: "Main Camera", position: [0, 2, -6], rotation: [10, 0, 0] },
        why: "modify writes Transform TRS directly and reports which fields changed."
      },
      {
        tool: "manage_screenshot",
        action: "capture_specific_camera",
        params: { camera_name: "Main Camera", width: 640, height: 360 },
        why: "Renders exactly what that camera sees, returned inline as an image."
      }
    ],
    verify: ["compare the screenshot with the requested framing", "read_console for missing-reference errors after moving objects"],
    pitfalls: [
      "position/rotation are LOCAL transform values; parenting changes their meaning.",
      "Edits are blocked in Play Mode — stop first, otherwise the change is discarded."
    ]
  },
  {
    id: "diagnose-console",
    goal: "Find why something is broken and locate the owning script",
    when: ["the user reports an error, a blank UI, a missing object or a failed build", "a previous tool call failed"],
    steps: [
      {
        tool: "read_console",
        action: "get",
        params: { types: ["error"], count: 30, format: "detailed", includeStacktrace: true },
        why: "Returns file/line and a diagnosis[] entry with likely_cause and fix."
      },
      {
        tool: "inspect_script",
        params: { path: "Assets/Scripts/Example.cs" },
        why: "Use the file from the console entry to see fields, lifecycle methods and where the script is attached — no need to open the file."
      },
      {
        tool: "manage_gameobject",
        action: "get_components",
        params: { target: "Canvas/Example" },
        why: "Check the object-side state: null object_ref fields are the most common NullReference cause."
      }
    ],
    verify: ["fix, then manage_editor.request_compile (or refresh) and read_console again", "confirm the error count dropped to zero"],
    pitfalls: [
      "Only messages logged while the bridge is loaded are captured.",
      "If the error is a compile error, no scene script runs at all until it is fixed."
    ]
  },
  {
    id: "find-feature-owner",
    goal: "Find which script or asset implements a feature the user mentions",
    when: ["the user refers to a system by name (settings, lobby, glitch, inventory)", "you need the right component before changing anything"],
    steps: [
      {
        tool: "project_map",
        params: { maxComponents: 60 },
        why: "Lists custom components with role, summary, serialized fields and where they are used."
      },
      {
        tool: "inspect_script",
        params: { type: "SettingsWindowManager" },
        why: "Exact fields and public methods for that component, plus attachment locations."
      },
      {
        tool: "find_usage",
        params: { path: "Assets/Scripts/SettingsWindowManager.cs" },
        why: "Shows which scene objects/prefabs carry it, so you can read live values next."
      },
      {
        tool: "manage_gameobject",
        action: "get_components",
        params: { target: "Canvas/SettingsRoot" },
        why: "Read the live serialized values and the exact field names to modify."
      }
    ],
    verify: ["quote the script path and field names back to the user", "only then make the edit"],
    pitfalls: [
      "Dynamic loading is invisible to find_usage; zero references is not proof of unused.",
      "project_map only covers loaded scenes for used_in_scene; load the relevant scene first."
    ]
  },
  {
    id: "change-animator",
    goal: "Change an animation's speed, state or a parameter",
    when: ["user asks to slow down, speed up, loop or switch an animation"],
    steps: [
      {
        tool: "manage_gameobject",
        action: "get_components",
        params: { target: "Canvas/Title" },
        why: "Find the Animator component and its serialized fields (m_Speed, m_ApplyRootMotion...)."
      },
      {
        tool: "manage_gameobject",
        action: "set_component_property",
        params: { target: "Canvas/Title", componentName: "Animator", propertyName: "m_Speed", propertyValue: 0.5 },
        why: "m_Speed is the serialized speed field; Animator parameters are runtime-only and need Play Mode scripting."
      },
      {
        tool: "exec_editor_script",
        params: {
          summary: "inspect animator controller",
          script:
            'var animator = GameObject.Find("Canvas/Title").GetComponent<Animator>();\n' +
            'return animator != null && animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "none";'
        },
        why: "Confirms which controller drives the object before editing clips or states."
      }
    ],
    verify: ["enter Play Mode with manage_editor.play and observe", "read_console for Animator warnings"],
    pitfalls: [
      "AnimationClip curves are assets; changing them needs exec_editor_script today.",
      "Animator parameters only exist at runtime; they cannot be written from edit mode."
    ]
  }
];

export function recipesJson(): string {
  return JSON.stringify(
    {
      count: RECIPES.length,
      note: "Each recipe is a verified call order. Replace example paths/targets with values from project_map, get_hierarchy or find.",
      recipes: RECIPES
    },
    null,
    2
  );
}
