using System;
using Lament.VisualNovel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class NovelPrologueScenePolish
{
    [MenuItem("工具/视觉小说/调整序章界面可读性")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("先停止播放");
        var scene = EditorSceneManager.OpenScene(NovelPrologueSceneSetup.ScenePath, OpenSceneMode.Single);
        var dialog = GameObject.Find("Canvas/对话框");
        if (dialog == null) throw new InvalidOperationException("找不到对话框");
        var image = dialog.GetComponent<Image>();
        if (image != null) image.color = new Color(.055f, .062f, .078f, .96f);
        var status = GameObject.Find("Canvas/状态");
        if (status != null)
        {
            var text = status.GetComponent<TMPro.TMP_Text>();
            if (text != null) text.text = "序章";
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[VisualNovel] 序章界面可读性已调整（对话框底色、顶部标题）");
    }
}
