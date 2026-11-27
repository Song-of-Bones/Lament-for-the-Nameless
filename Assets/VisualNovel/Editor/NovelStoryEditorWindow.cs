using System;
using System.Collections.Generic;
using Lament.VisualNovel;
using UnityEditor;
using UnityEngine;

public sealed class NovelStoryEditorWindow : EditorWindow
{
    private NovelStoryAsset story;
    private UnityEditor.Editor inspector;
    private UnityEngine.Object inspected;
    private Vector2 listScroll;
    private Vector2 detailScroll;
    private List<string> diagnostics = new List<string>();

    [MenuItem("工具/视觉小说/剧情编辑器")]
    public static void Open() => GetWindow<NovelStoryEditorWindow>("剧情编辑器").Show();

    private void OnSelectionChange()
    {
        if (Selection.activeObject is NovelStoryAsset selected) { story = selected; inspected = story; diagnostics.Clear(); Repaint(); }
    }

    private void OnGUI()
    {
        var selected = (NovelStoryAsset)EditorGUILayout.ObjectField("剧情", story, typeof(NovelStoryAsset), false);
        if (selected != story) { story = selected; inspected = null; diagnostics.Clear(); }
        if (story == null) { EditorGUILayout.HelpBox("通过 Assets > Create > 视觉小说 > 剧情 创建资源，或选择已有剧情。", MessageType.Info); return; }
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("剧情属性")) inspected = story;
        if (GUILayout.Button("新增节点")) AddNode();
        if (GUILayout.Button("校验"))
        {
            diagnostics = NovelStoryValidator.Validate(story);
            diagnostics.AddRange(NovelStoryValidator.Unreachable(story));
            var ids = new HashSet<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:NovelStoryAsset"))
            {
                var other = AssetDatabase.LoadAssetAtPath<NovelStoryAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (!ids.Add(other.storyId)) diagnostics.Add("跨资源重复 storyId：" + other.storyId);
            }
            Debug.Log("[VisualNovel] 校验完成，诊断数：" + diagnostics.Count, story);
        }
        if (GUILayout.Button("保存")) AssetDatabase.SaveAssets();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.BeginHorizontal();
        listScroll = EditorGUILayout.BeginScrollView(listScroll, GUILayout.Width(220));
        foreach (var node in story.nodes)
            if (node != null && GUILayout.Button(node.nodeId + " / " + node.type)) inspected = node;
        EditorGUILayout.EndScrollView();
        detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
        if (inspected == null) inspected = story;
        UnityEditor.Editor.CreateCachedEditor(inspected, null, ref inspector);
        inspector.OnInspectorGUI();
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndHorizontal();
        foreach (var diagnostic in diagnostics) EditorGUILayout.HelpBox(diagnostic, MessageType.Warning);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("从入口预览")) Preview(null);
            using (new EditorGUI.DisabledScope(!(inspected is NovelNodeAsset)))
                if (GUILayout.Button("从选中节点预览")) Preview(inspected as NovelNodeAsset);
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.HelpBox("节点预览会重置会话，使用默认变量，不推演此前分支或演出；不会改写剧情入口或保存正式进度。停止播放后恢复原有起始场景设置。", MessageType.Info);
    }

    private void Preview(NovelNodeAsset entry)
    {
        try { NovelStoryPreview.Play(story, entry); }
        catch (Exception ex) { diagnostics = new List<string> { ex.Message }; }
    }

    private void AddNode()
    {
        var node = CreateInstance<NovelNodeAsset>();
        node.nodeId = Guid.NewGuid().ToString("N");
        node.name = "节点_" + node.nodeId.Substring(0, 8);
        Undo.RegisterCreatedObjectUndo(node, "新增剧情节点");
        AssetDatabase.AddObjectToAsset(node, story);
        Undo.RecordObject(story, "加入剧情节点");
        story.nodes.Add(node);
        if (story.entryNode == null) story.entryNode = node;
        EditorUtility.SetDirty(story);
        AssetDatabase.SaveAssets();
        inspected = node;
    }

    private void OnDisable() { if (inspector != null) DestroyImmediate(inspector); }
}
