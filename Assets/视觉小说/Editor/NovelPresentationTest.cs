using System;
using System.Collections.Generic;
using System.IO;
using Lament.VisualNovel;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class NovelPresentationTest
{
    private const string Key = "Lament.VisualNovel.PresentationTest";
    private static readonly List<UnityEngine.Object> allocated = new List<UnityEngine.Object>();
    private static double deadline;
    private static double next;
    private static int step;
    private static string result;
    private static NovelStoryAsset story;
    private static Sprite sprite;
    private static AudioClip clip;
    private static NovelSaveData saved;
    private static int pausedSamples;
    private static Color pausedOverlay;

    static NovelPresentationTest()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
            {
                step = 0;
                result = "";
                deadline = EditorApplication.timeSinceStartup + 60;
                next = EditorApplication.timeSinceStartup + 1;
                Application.runInBackground = true;
                EditorApplication.update += Update;
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

    [MenuItem("工具/视觉小说/运行演出恢复测试")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先停止播放");
        var source = AssetDatabase.LoadAssetAtPath<NovelStoryAsset>(NovelProjectSetup.StoryPath);
        SessionState.SetBool(Key, true);
        try
        {
            NovelStoryPreview.Play(source, source.FindNode("intro"));
            if (!EditorApplication.isPlayingOrWillChangePlaymode) SessionState.SetBool(Key, false);
        }
        catch { SessionState.SetBool(Key, false); throw; }
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        result += "PASS " + message + "\n";
    }

    private static NovelNodeAsset Node(string id, NovelNodeType type)
    {
        var node = ScriptableObject.CreateInstance<NovelNodeAsset>();
        allocated.Add(node);
        node.nodeId = id;
        node.type = type;
        story.nodes.Add(node);
        return node;
    }

    private static void Prepare(NovelPlayer player)
    {
        story = ScriptableObject.CreateInstance<NovelStoryAsset>();
        allocated.Add(story);
        story.storyId = "presentation-test";
        story.displayName = "演出自动化测试";
        var texture = new Texture2D(2, 2);
        allocated.Add(texture);
        texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
        texture.Apply();
        sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), new Vector2(.5f, .5f));
        allocated.Add(sprite);
        // Silent, in-memory fixtures exercise bindings without generating or buying game assets.
        clip = AudioClip.Create("静音测试夹具", 44100 * 10, 1, 44100, false);
        allocated.Add(clip);
        story.resources.Add(new NovelResourceDefinition { id = "image", image = sprite });
        story.resources.Add(new NovelResourceDefinition { id = "audio", audio = clip });
        var bg = Node("bg", NovelNodeType.Command); bg.command = NovelCommandType.Background; bg.resourceId = "image";
        var portrait = Node("portrait", NovelNodeType.Command); portrait.command = NovelCommandType.Portrait; portrait.resourceId = "image"; portrait.portraitSlot = 2;
        var music = Node("music", NovelNodeType.Command); music.command = NovelCommandType.Music; music.resourceId = "audio";
        var sound = Node("sound", NovelNodeType.Command); sound.command = NovelCommandType.Sound; sound.resourceId = "audio";
        var fade = Node("fade", NovelNodeType.Command); fade.command = NovelCommandType.Fade; fade.color = new Color(.1f, .2f, .3f, .4f); fade.seconds = 4;
        var text = Node("dialogue", NovelNodeType.Dialogue); text.text = "演出恢复测试"; text.voice = clip;
        var hide = Node("hide", NovelNodeType.Command); hide.command = NovelCommandType.HidePortrait; hide.portraitSlot = 2;
        var stop = Node("stop", NovelNodeType.Command); stop.command = NovelCommandType.StopMusic;
        var end = Node("end", NovelNodeType.End); end.returnToLobby = false;
        bg.next = portrait; portrait.next = music; music.next = sound; sound.next = fade; fade.next = text;
        text.next = hide; hide.next = stop; stop.next = end;
        story.entryNode = bg;
        player.StartStory(story);
    }

    private static void Update()
    {
        if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup < next) return;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("演出测试超时，步骤=" + step);
            var player = UnityEngine.Object.FindAnyObjectByType<NovelPlayer>();
            var view = UnityEngine.Object.FindAnyObjectByType<NovelPresentationController>();
            if (player == null || player.Session == null || view == null) return;
            switch (step)
            {
                case 0:
                    if (player.Session.Current?.nodeId != "intro") return;
                    Prepare(player);
                    step++;
                    break;
                case 1:
                    Check(view.background.sprite == sprite, "背景命令绑定资源");
                    Check(view.portraits[2].sprite == sprite && view.portraits[2].enabled, "立绘命令使用正确槽位");
                    Check(view.music.clip == clip && view.music.isPlaying, "音乐命令开始循环播放");
                    Check(view.voice.clip == clip && view.voice.isPlaying, "对话语音开始播放");
                    Check(view.sound.isPlaying, "一次性音效命令开始播放");
                    player.Suspended = true;
                    step++;
                    break;
                case 2:
                    Check(!view.voice.isPlaying, "暂停后语音不继续播放");
                    pausedSamples = view.voice.timeSamples;
                    pausedOverlay = view.overlay.color;
                    step++;
                    next = EditorApplication.timeSinceStartup + .7;
                    return;
                case 3:
                    Check(Math.Abs(view.voice.timeSamples - pausedSamples) < 512, "暂停时语音采样位置保持不变");
                    Check(view.overlay.color == pausedOverlay, "暂停时渐变遮罩保持不变");
                    player.Suspended = false;
                    step++;
                    break;
                case 4:
                    Check(view.voice.isPlaying, "恢复后语音继续播放");
                    Check(view.overlay.color != pausedOverlay, "恢复后渐变继续执行");
                    saved = player.Session.Snapshot();
                    view.background.sprite = null;
                    view.portraits[2].enabled = false;
                    view.music.Stop();
                    view.music.clip = null;
                    view.sound.Stop();
                    view.overlay.color = Color.white;
                    player.Resume(saved);
                    step++;
                    break;
                case 5:
                    Check(view.background.sprite == sprite, "读档恢复背景");
                    Check(view.portraits[2].enabled && view.portraits[2].sprite == sprite, "读档恢复立绘与槽位");
                    Check(view.music.isPlaying && view.music.clip == clip, "读档恢复音乐");
                    Check(view.overlay.color == saved.presentation.overlay, "读档恢复渐变最终状态");
                    Check(!view.sound.isPlaying && !view.voice.isPlaying, "读档不重播一次性音效和对话语音");
                    player.Session.CompleteText();
                    player.Continue();
                    step++;
                    break;
                case 6:
                    Check(!view.portraits[2].enabled, "隐藏立绘命令生效");
                    Check(!view.music.isPlaying && view.music.clip == null, "停止音乐命令生效");
                    Check(player.Session.Status == NovelPlaybackStatus.Ended, "演出测试正常到达结束节点");
                    Finish(true);
                    return;
            }
            next = EditorApplication.timeSinceStartup + .25;
        }
        catch (Exception ex) { result += "FAIL " + ex + "\n"; Finish(false); }
    }

    private static void Finish(bool passed)
    {
        EditorApplication.update -= Update;
        SessionState.SetBool(Key, false);
        Directory.CreateDirectory(".codely-cli/test-results");
        File.WriteAllText(".codely-cli/test-results/visual-novel-presentation.txt", result);
        if (passed) Debug.Log("[VisualNovel] 演出恢复测试全部通过\n" + result);
        else Debug.LogError("[VisualNovel] 演出恢复测试失败\n" + result);
        EditorApplication.ExitPlaymode();
    }
}
