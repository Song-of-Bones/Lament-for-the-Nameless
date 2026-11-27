using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

public static class NovelLobbySetup
{
    [MenuItem("工具/视觉小说/接入当前大厅")]
    public static void Connect()
    {
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != "Assets/Scenes/Lobby.unity")
            throw new InvalidOperationException("请在编辑模式打开游戏大厅后接入");
        var start = GameObject.Find("Canvas/菜单/开始游戏").GetComponent<UIMenuButton>();
        var resume = GameObject.Find("Canvas/菜单/继续游戏").GetComponent<UIMenuButton>();
        var entry = start.GetComponent<NovelEntryPoint>();
        if (entry == null) entry = Undo.AddComponent<NovelEntryPoint>(start.gameObject);
        ConnectEvent(start, entry, entry.StartNewGame, nameof(NovelEntryPoint.StartNewGame));
        ConnectEvent(resume, entry, entry.ContinueGame, nameof(NovelEntryPoint.ContinueGame));
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[VisualNovel] 已接入大厅开始/继续按钮；保留已有持久事件和窗口设置");
    }

    private static void ConnectEvent(UIMenuButton button, NovelEntryPoint entry, UnityAction action, string method)
    {
        var data = new SerializedObject(button);
        if (!string.IsNullOrEmpty(data.FindProperty("打开窗口ID").stringValue))
            throw new InvalidOperationException(button.name + " 已配置窗口跳转，请先确认入口冲突，未覆盖原配置");
        var field = typeof(UIMenuButton).GetField("点击结束", BindingFlags.Instance | BindingFlags.NonPublic);
        var click = (UnityEvent)field.GetValue(button);
        if (click == null) { click = new UnityEvent(); field.SetValue(button, click); }
        for (int i = 0; i < click.GetPersistentEventCount(); i++)
            if (click.GetPersistentTarget(i) == entry && click.GetPersistentMethodName(i) == method) return;
        Undo.RecordObject(button, "连接视觉小说入口");
        UnityEventTools.AddPersistentListener(click, action);
        EditorUtility.SetDirty(button);
    }
}
