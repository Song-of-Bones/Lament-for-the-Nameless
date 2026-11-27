using System;
using System.Collections.Generic;
using Lament.VisualNovel;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class SettingsPageBuilder
{
    private const string ScenePath = "Assets/Scenes/Settings.unity";
    private const string ContentPath = "Canvas/面板/滚动区/视口/内容";

    private static readonly Color SliderBack = new Color(.10f, .11f, .14f, 1f);
    private static readonly Color SliderFill = new Color(.52f, .66f, .92f, 1f);
    private static readonly Color HandleColor = new Color(.92f, .94f, .98f, 1f);
    private static readonly Color LabelColor = new Color(.86f, .88f, .92f, 1f);
    private static readonly Color ValueColor = new Color(.62f, .70f, .86f, 1f);

    [MenuItem("工具/项目/生成设置页控件")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先停止播放");
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var content = GameObject.Find(ContentPath);
        if (content == null) throw new InvalidOperationException("找不到设置内容区：" + ContentPath);
        for (int i = content.transform.childCount - 1; i >= 0; i--)
        {
            var child = content.transform.GetChild(i).gameObject;
            if (child.name.StartsWith("Setting_")) UnityEngine.Object.DestroyImmediate(child);
        }
        var layout = content.GetComponent<VerticalLayoutGroup>();
        if (layout != null)
        {
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 6f;
            layout.padding = new RectOffset(0, 0, 8, 12);
        }
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/SourceHanSans-Small SDF.asset");
        if (font == null) throw new InvalidOperationException("缺少中文字体资源");
        var intro = GameObject.Find(ContentPath + "/正文")?.GetComponent<TMP_Text>();
        if (intro != null) intro.text = "阅读与声音";

        var speed = SliderRow(content.transform, font, "Setting_TextSpeed", "文字速度", GameSettings.MinTextSpeed, GameSettings.MaxTextSpeed, GameSettings.DefaultTextSpeed, out var speedValue);
        var auto = SliderRow(content.transform, font, "Setting_AutoDelay", "自动播放间隔", GameSettings.MinAutoDelay, GameSettings.MaxAutoDelay, GameSettings.DefaultAutoDelay, out var autoValue);
        var music = SliderRow(content.transform, font, "Setting_Music", "音乐音量", 0f, 1f, 1f, out var musicValue);
        var ambient = SliderRow(content.transform, font, "Setting_Ambient", "环境音音量", 0f, 1f, 1f, out var ambientValue);
        var voice = SliderRow(content.transform, font, "Setting_Voice", "语音音量", 0f, 1f, 1f, out var voiceValue);
        var sound = SliderRow(content.transform, font, "Setting_Sound", "音效音量", 0f, 1f, 1f, out var soundValue);
        var fullscreen = ToggleRow(content.transform, font, "Setting_Fullscreen", "全屏显示");
        var reset = ButtonRow(content.transform, font, "Setting_Reset", "恢复默认设置");

        var host = GameObject.Find("Canvas/窗口控制器");
        if (host == null) throw new InvalidOperationException("找不到窗口控制器");
        var panel = host.GetComponent<NovelSettingsPanel>();
        if (panel == null) panel = host.AddComponent<NovelSettingsPanel>();
        var serialized = new SerializedObject(panel);
        Assign(serialized, "textSpeed", speed);
        Assign(serialized, "autoDelay", auto);
        Assign(serialized, "musicVolume", music);
        Assign(serialized, "ambientVolume", ambient);
        Assign(serialized, "voiceVolume", voice);
        Assign(serialized, "soundVolume", sound);
        Assign(serialized, "fullscreen", fullscreen);
        Assign(serialized, "reset", reset);
        Assign(serialized, "textSpeedValue", speedValue);
        Assign(serialized, "autoDelayValue", autoValue);
        Assign(serialized, "musicValue", musicValue);
        Assign(serialized, "ambientValue", ambientValue);
        Assign(serialized, "voiceValue", voiceValue);
        Assign(serialized, "soundValue", soundValue);
        serialized.ApplyModifiedPropertiesWithoutUndo();

        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)content.transform);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Settings] 设置页控件已生成并绑定");
    }

    private static void Assign(SerializedObject serialized, string field, UnityEngine.Object value)
    {
        var property = serialized.FindProperty(field);
        if (property == null) throw new InvalidOperationException("NovelSettingsPanel 缺少字段：" + field);
        property.objectReferenceValue = value;
    }

    private static RectTransform Row(Transform parent, string name, float height)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        var element = go.AddComponent<LayoutElement>();
        element.preferredHeight = height;
        element.minHeight = height;
        element.flexibleWidth = 1;
        return rect;
    }

    private static TMP_Text Label(Transform parent, TMP_FontAsset font, string name, Vector2 min, Vector2 max, string text, float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = min; rect.anchorMax = max;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var label = go.AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.text = text;
        label.fontSize = size;
        label.color = color;
        label.alignment = align;
        label.raycastTarget = false;
        return label;
    }

    private static Slider SliderRow(Transform parent, TMP_FontAsset font, string name, string label, float min, float max, float value, out TMP_Text valueLabel)
    {
        var root = Row(parent, name, 72f);
        Label(root, font, "Label", new Vector2(0, .55f), new Vector2(.6f, 1f), label, 26, LabelColor);
        valueLabel = Label(root, font, "Value", new Vector2(.6f, .55f), new Vector2(1f, 1f), "", 24, ValueColor, TextAlignmentOptions.MidlineRight);

        var sliderRoot = new GameObject("Slider", typeof(RectTransform));
        var sliderRect = sliderRoot.GetComponent<RectTransform>();
        sliderRect.SetParent(root, false);
        sliderRect.anchorMin = new Vector2(0, .10f);
        sliderRect.anchorMax = new Vector2(1, .46f);
        sliderRect.offsetMin = sliderRect.offsetMax = Vector2.zero;

        var background = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var backgroundRect = background.GetComponent<RectTransform>();
        backgroundRect.SetParent(sliderRect, false);
        backgroundRect.anchorMin = new Vector2(0, .32f); backgroundRect.anchorMax = new Vector2(1, .68f);
        backgroundRect.offsetMin = backgroundRect.offsetMax = Vector2.zero;
        var backgroundImage = background.GetComponent<Image>();
        backgroundImage.color = SliderBack;
        backgroundImage.raycastTarget = true;

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        var fillAreaRect = fillArea.GetComponent<RectTransform>();
        fillAreaRect.SetParent(sliderRect, false);
        fillAreaRect.anchorMin = new Vector2(0, .32f); fillAreaRect.anchorMax = new Vector2(1, .68f);
        fillAreaRect.offsetMin = fillAreaRect.offsetMax = Vector2.zero;
        var fill = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var fillRect = fill.GetComponent<RectTransform>();
        fillRect.SetParent(fillAreaRect, false);
        fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
        var fillImage = fill.GetComponent<Image>();
        fillImage.color = SliderFill;
        fillImage.raycastTarget = false;

        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        var handleAreaRect = handleArea.GetComponent<RectTransform>();
        handleAreaRect.SetParent(sliderRect, false);
        handleAreaRect.anchorMin = Vector2.zero; handleAreaRect.anchorMax = Vector2.one;
        handleAreaRect.offsetMin = new Vector2(14, 0); handleAreaRect.offsetMax = new Vector2(-14, 0);
        var handle = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var handleRect = handle.GetComponent<RectTransform>();
        handleRect.SetParent(handleAreaRect, false);
        handleRect.sizeDelta = new Vector2(28, 28);
        var handleImage = handle.GetComponent<Image>();
        handleImage.color = HandleColor;
        handleImage.raycastTarget = true;

        var slider = sliderRoot.AddComponent<Slider>();
        slider.fillRect = fillRect;
        slider.handleRect = handleRect;
        slider.targetGraphic = handleImage;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;
        slider.SetValueWithoutNotify(value);
        return slider;
    }

    private static Toggle ToggleRow(Transform parent, TMP_FontAsset font, string name, string label)
    {
        var root = Row(parent, name, 56f);
        Label(root, font, "Label", new Vector2(0, 0), new Vector2(.7f, 1f), label, 26, LabelColor);
        var toggleRoot = new GameObject("Toggle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Toggle));
        var toggleRect = toggleRoot.GetComponent<RectTransform>();
        toggleRect.SetParent(root, false);
        toggleRect.anchorMin = new Vector2(.82f, .5f); toggleRect.anchorMax = new Vector2(.82f, .5f);
        toggleRect.pivot = new Vector2(.5f, .5f);
        toggleRect.sizeDelta = new Vector2(34, 34);
        var box = toggleRoot.GetComponent<Image>();
        box.color = SliderBack;

        var check = new GameObject("Check", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var checkRect = check.GetComponent<RectTransform>();
        checkRect.SetParent(toggleRect, false);
        checkRect.anchorMin = new Vector2(.18f, .18f); checkRect.anchorMax = new Vector2(.82f, .82f);
        checkRect.offsetMin = checkRect.offsetMax = Vector2.zero;
        var checkImage = check.GetComponent<Image>();
        checkImage.color = SliderFill;
        checkImage.raycastTarget = false;

        var toggle = toggleRoot.GetComponent<Toggle>();
        toggle.targetGraphic = box;
        toggle.graphic = checkImage;
        toggle.SetIsOnWithoutNotify(true);
        return toggle;
    }

    private static Button ButtonRow(Transform parent, TMP_FontAsset font, string name, string label)
    {
        var root = Row(parent, name, 60f);
        var buttonRoot = new GameObject("Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        var rect = buttonRoot.GetComponent<RectTransform>();
        rect.SetParent(root, false);
        rect.anchorMin = new Vector2(.25f, .1f); rect.anchorMax = new Vector2(.75f, .9f);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var background = buttonRoot.GetComponent<Image>();
        background.color = new Color(.16f, .19f, .25f, 1f);
        var button = buttonRoot.GetComponent<Button>();
        button.targetGraphic = background;
        var text = Label(buttonRoot.transform, font, "Label", Vector2.zero, Vector2.one, label, 26, LabelColor, TextAlignmentOptions.Center);
        text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
        return button;
    }
}
