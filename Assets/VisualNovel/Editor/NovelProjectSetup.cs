using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lament.VisualNovel;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class NovelProjectSetup
{
    public const string StoryPath = "Assets/VisualNovel/Stories/FrameworkDemo.asset";
    public const string ScenePath = "Assets/VisualNovel/Scenes/NovelRuntime.unity";
    private static TMP_FontAsset font;
    private static Sprite panelSprite;

    [MenuItem("工具/视觉小说/检查中文支持")]
    public static void CheckFonts()
    {
        var fallbacks = TMP_Settings.fallbackFontAssets;
        bool ready = fallbacks != null && fallbacks.Any(f => f != null && f.HasCharacter('中', true, false));
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/SourceHanSans-Small SDF.asset");
        Debug.Log("[VisualNovel] TMP_CHINESE_FALLBACK=" + ready + " EXISTING_LOCAL_FONT=" + (existing != null && existing.HasCharacter('中', true, false)));
    }

    [MenuItem("工具/视觉小说/创建示例与运行场景")]
    public static void CreateDemo()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先停止播放");
        if (File.Exists(ScenePath) || File.Exists(StoryPath)) throw new InvalidOperationException("示例已存在，拒绝覆盖。请直接编辑已有剧情和场景。");
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/SourceHanSans-Small SDF.asset");
        if (font == null || !font.HasCharacter('中', true, false)) throw new InvalidOperationException("现有中文字体不可用，需要先取得中文字体配置授权");
        panelSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/RoundedPanel.png");
        Directory.CreateDirectory("Assets/VisualNovel/Stories");
        Directory.CreateDirectory("Assets/VisualNovel/Scenes");
        AssetDatabase.Refresh();
        var story = CreateStory();
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camera.tag = "MainCamera";
            camera.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
            camera.GetComponent<Camera>().backgroundColor = Color.black;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            var root = new GameObject("视觉小说", typeof(NovelPlayer), typeof(NovelPresentationController));
            var player = root.GetComponent<NovelPlayer>();
            var view = root.GetComponent<NovelPresentationController>();
            var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2400, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var canvasRoot = canvasObject.transform;
            view.background = Panel(canvasRoot, "背景", Vector2.zero, Vector2.one, Color.black).GetComponent<Image>();
            view.portraits = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                view.portraits[i] = Panel(canvasRoot, "立绘槽" + i, new Vector2(i / 3f, .2f), new Vector2((i + 1) / 3f, .95f), Color.clear).GetComponent<Image>();
                view.portraits[i].preserveAspect = true;
            }
            view.overlay = Panel(canvasRoot, "演出遮罩", Vector2.zero, Vector2.one, Color.clear).GetComponent<Image>();
            view.overlay.raycastTarget = false;
            var dialog = Panel(canvasRoot, "对话框", new Vector2(.035f, .075f), new Vector2(.965f, .32f), new Color(.015f, .018f, .025f, .95f));
            view.advance = dialog.gameObject.AddComponent<Button>();
            view.speaker = Text(dialog, "角色名", new Vector2(.025f, .74f), new Vector2(.95f, .97f), "", 34);
            view.body = Text(dialog, "正文", new Vector2(.025f, .12f), new Vector2(.975f, .72f), "", 34);
            view.body.richText = true;
            Text(dialog, "继续提示", new Vector2(.84f, 0), new Vector2(.98f, .14f), "空格 / 点击继续", 18);
            view.status = Text(canvasRoot, "状态", new Vector2(.03f, .92f), new Vector2(.97f, .98f), "框架功能演示 · 非正式序章", 23);
            view.choiceRoot = Panel(canvasRoot, "选项", new Vector2(.25f, .36f), new Vector2(.75f, .75f), Color.clear);
            var layout = view.choiceRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 16;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            view.choiceTemplate = Button(view.choiceRoot, "选项模板", Vector2.zero, Vector2.one, "选项");
            view.choiceTemplate.gameObject.AddComponent<LayoutElement>().preferredHeight = 72;
            view.choiceTemplate.gameObject.SetActive(false);
            var controls = new[] { "自动", "已读快进", "历史", "存读档", "快速存档", "快速读档", "设置", "返回大厅" };
            var buttons = new Button[controls.Length];
            for (int i = 0; i < controls.Length; i++) buttons[i] = Button(canvasRoot, controls[i], new Vector2(.035f + i * .117f, .012f), new Vector2(.145f + i * .117f, .062f), controls[i]);
            UnityEventTools.AddPersistentListener(buttons[0].onClick, view.ToggleAuto);
            UnityEventTools.AddPersistentListener(buttons[1].onClick, view.ToggleSkip);
            UnityEventTools.AddPersistentListener(buttons[2].onClick, view.OpenHistory);
            UnityEventTools.AddPersistentListener(buttons[3].onClick, view.OpenSlots);
            UnityEventTools.AddPersistentListener(buttons[4].onClick, view.QuickSave);
            UnityEventTools.AddPersistentListener(buttons[5].onClick, view.QuickLoad);
            UnityEventTools.AddPersistentListener(buttons[7].onClick, view.ReturnToLobby);
            view.historyPanel = Panel(canvasRoot, "历史窗口", new Vector2(.08f, .09f), new Vector2(.92f, .9f), new Color(.02f, .025f, .035f, .99f)).gameObject;
            var viewport = Panel(view.historyPanel.transform, "Viewport", new Vector2(.03f, .1f), new Vector2(.97f, .9f), Color.clear);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Rect(viewport, "Content", new Vector2(0, 1), Vector2.one);
            content.pivot = new Vector2(.5f, 1);
            var fit = content.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            view.historyText = content.gameObject.AddComponent<TextMeshProUGUI>();
            view.historyText.font = font;
            view.historyText.fontSize = 28;
            view.historyText.raycastTarget = false;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            var closeHistory = Button(view.historyPanel.transform, "关闭历史", new Vector2(.8f, .92f), new Vector2(.97f, .99f), "关闭");
            UnityEventTools.AddPersistentListener(closeHistory.onClick, view.ClosePanels);
            view.historyPanel.SetActive(false);
            view.slotsPanel = Panel(canvasRoot, "存读档窗口", new Vector2(.1f, .12f), new Vector2(.9f, .87f), new Color(.02f, .025f, .035f, .99f)).gameObject;
            view.slotLabels = new TMP_Text[3];
            for (int i = 0; i < 3; i++)
            {
                float y = .64f - i * .25f;
                view.slotLabels[i] = Text(view.slotsPanel.transform, "槽位" + (i + 1), new Vector2(.04f, y), new Vector2(.65f, y + .2f), "槽位", 26);
                var save = Button(view.slotsPanel.transform, "保存" + (i + 1), new Vector2(.67f, y + .04f), new Vector2(.8f, y + .17f), "保存");
                var load = Button(view.slotsPanel.transform, "读取" + (i + 1), new Vector2(.82f, y + .04f), new Vector2(.96f, y + .17f), "读取");
                UnityEventTools.AddIntPersistentListener(save.onClick, view.SaveSlot, i + 1);
                UnityEventTools.AddIntPersistentListener(load.onClick, view.LoadSlot, i + 1);
            }
            var closeSlots = Button(view.slotsPanel.transform, "关闭存档", new Vector2(.8f, .9f), new Vector2(.96f, .98f), "关闭");
            UnityEventTools.AddPersistentListener(closeSlots.onClick, view.ClosePanels);
            view.slotsPanel.SetActive(false);
            view.music = root.AddComponent<AudioSource>(); view.music.loop = true; view.music.playOnAwake = false;
            view.voice = root.AddComponent<AudioSource>(); view.voice.playOnAwake = false;
            view.sound = root.AddComponent<AudioSource>(); view.sound.playOnAwake = false;
            var windows = new GameObject("窗口管理", typeof(WindowDatabase), typeof(SettingsWindowManager), typeof(NovelSettingsAdapter));
            windows.GetComponent<WindowDatabase>().configs.Add(new WindowDatabase.WindowConfig { id = "novel_settings", sceneName = "Settings" });
            var adapter = windows.GetComponent<NovelSettingsAdapter>();
            adapter.player = player; adapter.manager = windows.GetComponent<SettingsWindowManager>();
            UnityEventTools.AddPersistentListener(buttons[6].onClick, adapter.OpenSettings);
            player.Configure(story, view);
            EditorSceneManager.SaveScene(scene, ScenePath);
            var build = EditorBuildSettings.scenes.ToList();
            if (!build.Any(s => s.path == ScenePath)) build.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = build.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("[VisualNovel] 示例、运行场景已创建；剧情校验：" + NovelStoryValidator.Validate(story).Count);
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
            SceneManager.SetActiveScene(previous);
        }
    }

    private static NovelStoryAsset CreateStory()
    {
        var story = ScriptableObject.CreateInstance<NovelStoryAsset>();
        story.storyId = "framework-demo-prologue";
        story.displayName = "序章 · 框架功能演示";
        story.variables.Add(new NovelVariableDefinition { id = "route", initial = new NovelValue { type = NovelValueType.Integer } });
        story.characters.Add(new NovelCharacterDefinition { id = "guide", displayName = "引导", nameColor = new Color(.75f, .85f, 1) });
        AssetDatabase.CreateAsset(story, StoryPath);
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Rooftop.png");
        if (sprite == null)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Rooftop.png");
            if (texture != null)
            {
                sprite = Sprite.Create(texture, new UnityEngine.Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f));
                sprite.name = "演示背景";
                AssetDatabase.AddObjectToAsset(sprite, story);
            }
        }
        if (sprite != null) story.resources.Add(new NovelResourceDefinition { id = "roof", image = sprite });
        var intro = Node(story, "intro", NovelNodeType.Dialogue, "这是一段框架功能演示，并非正式剧本。点击正文或按空格继续。");
        var second = Node(story, "instructions", NovelNodeType.Dialogue, "你可以随时保存进度、打开历史，或启用自动播放。接下来请选择一条路线。");
        var choice = Node(story, "choice", NovelNodeType.Choice, "请选择演示路线");
        var branch = Node(story, "branch", NovelNodeType.Conditional);
        var left = Node(story, "route-a", NovelNodeType.Dialogue, "你选择了路线 A。变量 route 的值为 1；这里可以验证分支存档恢复。");
        var right = Node(story, "route-b", NovelNodeType.Dialogue, "你选择了路线 B。变量 route 的值为 2；这里可以验证另一条分支。");
        var final = Node(story, "final", NovelNodeType.Dialogue, "演示即将结束并返回大厅。返回前，你可以保存到任一槽位，再从大厅继续游戏。");
        var end = Node(story, "end", NovelNodeType.End);
        intro.next = second; second.next = choice;
        choice.choices.Add(new NovelChoice { text = "路线 A", target = branch, operations = new List<NovelOperation> { new NovelOperation { variable = "route", value = new NovelValue { type = NovelValueType.Integer, integer = 1 } } } });
        choice.choices.Add(new NovelChoice { text = "路线 B", target = branch, operations = new List<NovelOperation> { new NovelOperation { variable = "route", value = new NovelValue { type = NovelValueType.Integer, integer = 2 } } } });
        branch.condition = new NovelCondition { variable = "route", value = new NovelValue { type = NovelValueType.Integer, integer = 1 } };
        branch.whenTrue = left; branch.whenFalse = right;
        left.next = right.next = final; final.next = end;
        story.entryNode = intro;
        if (sprite != null)
        {
            var bg = Node(story, "background", NovelNodeType.Command);
            bg.command = NovelCommandType.Background; bg.resourceId = "roof"; bg.next = intro; story.entryNode = bg;
        }
        foreach (var node in story.nodes) EditorUtility.SetDirty(node);
        EditorUtility.SetDirty(story);
        return story;
    }

    private static NovelNodeAsset Node(NovelStoryAsset story, string id, NovelNodeType type, string text = "")
    {
        var node = ScriptableObject.CreateInstance<NovelNodeAsset>();
        node.name = node.nodeId = id; node.type = type; node.text = text;
        if (type == NovelNodeType.Dialogue) node.characterId = "guide";
        AssetDatabase.AddObjectToAsset(node, story); story.nodes.Add(node); return node;
    }

    private static RectTransform Rect(Transform parent, string name, Vector2 min, Vector2 max)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero; return rect;
    }
    private static RectTransform Panel(Transform parent, string name, Vector2 min, Vector2 max, Color color)
    {
        var rect = Rect(parent, name, min, max); var image = rect.gameObject.AddComponent<Image>();
        image.color = color; image.raycastTarget = color.a > .5f; image.sprite = panelSprite; image.type = Image.Type.Sliced; return rect;
    }
    private static TMP_Text Text(Transform parent, string name, Vector2 min, Vector2 max, string value, float size)
    {
        var text = Rect(parent, name, min, max).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font; text.text = value; text.fontSize = size; text.color = Color.white; text.raycastTarget = false; return text;
    }
    private static Button Button(Transform parent, string name, Vector2 min, Vector2 max, string label)
    {
        var rect = Panel(parent, name, min, max, new Color(.09f, .1f, .13f, .98f));
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
        var text = Text(rect, "文本", new Vector2(.04f, .1f), new Vector2(.96f, .9f), label, 25);
        text.alignment = TextAlignmentOptions.Center; return button;
    }
}
