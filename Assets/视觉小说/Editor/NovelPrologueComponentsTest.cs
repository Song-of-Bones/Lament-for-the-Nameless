using System;
using System.Collections.Generic;
using System.IO;
using Lament.VisualNovel;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

[InitializeOnLoad]
public static class NovelPrologueComponentsTest
{
    private const string Key = "Lament.VisualNovel.PrologueTest";
    private static readonly List<UnityEngine.Object> allocated = new List<UnityEngine.Object>();
    private static int step;
    private static double next;
    private static double deadline;
    private static string result;
    private static Sprite sprite;
    private static AudioClip clip;
    private static NovelStoryAsset story;
    private static NovelSaveData saved;
    private static float pausedVolume;

    static NovelPrologueComponentsTest()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
            {
                step = 0; result = ""; next = EditorApplication.timeSinceStartup + 1;
                deadline = next + 60; EditorApplication.update += Update;
            }
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                EditorApplication.update -= Update;
                SessionState.SetBool(Key, false);
                foreach (var item in allocated) if (item != null) UnityEngine.Object.DestroyImmediate(item);
                allocated.Clear();
            }
        };
    }

    [MenuItem("工具/视觉小说/运行序章组件测试")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("先停止播放");
        var source = AssetDatabase.LoadAssetAtPath<NovelStoryAsset>(NovelProjectSetup.StoryPath);
        SessionState.SetBool(Key, true);
        try { NovelStoryPreview.Play(source, source.FindNode("intro")); }
        catch { SessionState.SetBool(Key, false); throw; }
        if (!EditorApplication.isPlayingOrWillChangePlaymode) SessionState.SetBool(Key, false);
    }

    private static NovelNodeAsset Node(string id, NovelNodeType type)
    {
        var node = ScriptableObject.CreateInstance<NovelNodeAsset>(); allocated.Add(node);
        node.nodeId = id; node.type = type; story.nodes.Add(node); return node;
    }
    private static void Check(bool value, string text)
    {
        if (!value) throw new InvalidOperationException(text);
        result += "PASS " + text + "\n";
    }
    private static void Prepare(NovelPlayer player)
    {
        story = ScriptableObject.CreateInstance<NovelStoryAsset>(); allocated.Add(story);
        story.storyId = "prologue-components-test";
        story.variables.Add(new NovelVariableDefinition { id = "name", initial = new NovelValue { type = NovelValueType.String, text = "勒特" } });
        var texture = new Texture2D(2, 2); allocated.Add(texture);
        texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white }); texture.Apply();
        sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * .5f); allocated.Add(sprite);
        clip = AudioClip.Create("环境音静音夹具", 44100 * 8, 1, 44100, false); allocated.Add(clip);
        story.resources.Add(new NovelResourceDefinition { id = "cg", image = sprite });
        story.resources.Add(new NovelResourceDefinition { id = "ambience", audio = clip });
        var input = Node("input", NovelNodeType.TextInput); input.text = "还记得自己的名字吗？"; input.inputVariable = "name";
        var bg = Node("bg", NovelNodeType.Command); bg.command = NovelCommandType.Background; bg.resourceId = "cg";
        var cg = Node("cg", NovelNodeType.Command); cg.command = NovelCommandType.ShowCG; cg.resourceId = "cg"; cg.seconds = .4f; cg.waitForCompletion = true;
        var camera = Node("camera", NovelNodeType.Command); camera.command = NovelCommandType.BackgroundTransform; camera.pose.position = new Vector2(80, 20); camera.pose.scale = Vector2.one * 1.2f; camera.seconds = .4f; camera.waitForCompletion = true;
        var extra = Node("extra-actor", NovelNodeType.Command); extra.command = NovelCommandType.Portrait; extra.resourceId = "cg"; extra.portraitSlot = 5;
        var ambient = Node("ambient", NovelNodeType.Command); ambient.command = NovelCommandType.Ambient; ambient.resourceId = "ambience";
        var volume = Node("volume", NovelNodeType.Command); volume.command = NovelCommandType.AudioVolume; volume.audioBus = NovelAudioBus.Ambient; volume.volume = .2f; volume.seconds = 3;
        var text = Node("text", NovelNodeType.Dialogue); text.text = "{name}，你记起来了。";
        var hide = Node("hide", NovelNodeType.Command); hide.command = NovelCommandType.HideCG;
        var stop = Node("stop", NovelNodeType.Command); stop.command = NovelCommandType.StopAmbient;
        var distort = Node("distort", NovelNodeType.Command); distort.command = NovelCommandType.Distortion; distort.color = new Color(.7f, .9f, 1, .5f); distort.seconds = .4f; distort.waitForCompletion = true;
        var shatter = Node("shatter", NovelNodeType.Command); shatter.command = NovelCommandType.Shatter; shatter.seconds = 1.2f; shatter.waitForCompletion = true;
        var end = Node("end", NovelNodeType.End); end.returnToLobby = false;
        input.next = bg; bg.next = cg; cg.next = camera; camera.next = extra; extra.next = ambient; ambient.next = volume; volume.next = text; text.next = hide; hide.next = stop; stop.next = distort; distort.next = shatter; shatter.next = end;
        story.entryNode = input; player.StartStory(story);
    }

    private static void Update()
    {
        if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup < next) return;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("序章组件测试超时 步骤=" + step);
            var player = UnityEngine.Object.FindAnyObjectByType<NovelPlayer>();
            var view = UnityEngine.Object.FindAnyObjectByType<NovelPresentationController>();
            if (player == null || player.Session == null || view == null) return;
            switch (step)
            {
                case 0:
                    if (player.Session.Current?.nodeId != "intro") return;
                    Prepare(player); step++; break;
                case 1:
                    Check(player.Session.Status == NovelPlaybackStatus.TextInput && view.NameInput.Panel.activeSelf, "命名输入UI实际显示");
                    Canvas.ForceUpdateCanvases();
                    var button = view.NameInput.Confirm;
                    var rect = (RectTransform)button.transform;
                    var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)), button = PointerEventData.InputButton.Left };
                    var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
                    Check(hits.Count > 0 && ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject) == button.gameObject, "命名确认按钮射线可点击");
                    view.NameInput.Field.text = "阿明";
                    ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
                    step++; break;
                case 2:
                    if (player.Session.Current.nodeId != "text") return;
                    Check(!view.NameInput.Panel.activeSelf && view.body.text == "阿明，你记起来了。", "输入提交后变量替换正文");
                    Check(view.Effects.CG.enabled && view.Effects.CG.sprite == sprite, "CG层图像显示");
                    Check(view.portraits.Length == 6 && view.portraits[5].sprite == sprite && view.portraits[5].enabled, "第六立绘槽显示且不限制三人同屏");
                    Check(view.background.sprite == sprite && view.background.rectTransform.localScale.x > 1.19f, "背景镜头独立缩放完成");
                    Check(view.AudioChannels.Ambient.clip == clip && view.AudioChannels.Ambient.isPlaying, "环境音独立循环播放");
                    Check(!view.music.isPlaying && view.music.clip == null, "环境音不占用BGM轨");
                    player.Suspended = true; pausedVolume = view.AudioChannels.Ambient.volume; step++; break;
                case 3:
                    Check(view.AudioChannels.Ambient.volume == pausedVolume, "暂停冻结环境音量渐变");
                    player.Suspended = false; step++; break;
                case 4:
                    Check(view.AudioChannels.Ambient.volume < pausedVolume, "恢复后环境音量继续渐变");
                    saved = player.Session.Snapshot(); player.Resume(saved);
                    step++; break;
                case 5:
                    Check(view.Effects.ActiveCount == 0 && view.Effects.CG.sprite == sprite, "读档恢复CG且取消瞬态演出");
                    Check(Mathf.Abs(view.AudioChannels.Ambient.volume - .2f) < .001f, "读档恢复最终音轨音量");
                    Check(view.background.rectTransform.anchoredPosition == new Vector2(80, 20), "读档恢复背景镜头偏移");
                    player.Session.CompleteText(); player.Continue();
                    Check(view.Effects.Busy("distortion"), "临时错位重影通道启动");
                    Check(!view.AudioChannels.Ambient.isPlaying && !view.Effects.CG.enabled, "停止环境音与隐藏CG生效");
                    step++; next = EditorApplication.timeSinceStartup + .5; return;
                case 6:
                    Check(player.Session.Current.nodeId == "shatter" && !view.Effects.Busy("distortion"), "错位演出结束清理并推进剧情");
                    Check(view.Effects.Busy("shatter"), "碎片转场独立通道启动");
                    step++; next = EditorApplication.timeSinceStartup + 1.3; return;
                case 7:
                    Check(player.Session.Status == NovelPlaybackStatus.Ended && !view.Effects.Busy("shatter"), "碎片转场结束并清理");
                    Finish(true); return;
            }
            next = EditorApplication.timeSinceStartup + .4;
        }
        catch (Exception ex) { result += "FAIL " + ex + "\n"; Finish(false); }
    }
    private static void Finish(bool passed)
    {
        EditorApplication.update -= Update; SessionState.SetBool(Key, false);
        Directory.CreateDirectory(".codely-cli/test-results"); File.WriteAllText(".codely-cli/test-results/visual-novel-prologue.txt", result);
        if (passed) Debug.Log("[VisualNovel] 序章组件测试全部通过\n" + result);
        else Debug.LogError("[VisualNovel] 序章组件测试失败\n" + result);
        EditorApplication.ExitPlaymode();
    }
}
