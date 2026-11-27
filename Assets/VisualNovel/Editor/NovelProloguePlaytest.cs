using System;
using System.IO;
using System.Linq;
using Lament.VisualNovel;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class NovelProloguePlaytest
{
    private const string Key = "Lament.VisualNovel.ProloguePlay";
    private static int visited;
    private static int screenshots;
    private static bool reachedSouvenir;
    private static int branchPhase;
    private static double next;
    private static double deadline;
    private static string result = "";

    static NovelProloguePlaytest()
    {
        // Polling is intentional: a static constructor can miss the one-shot play-mode event after a reload.
        EditorApplication.update += Poll;
    }

    [MenuItem("工具/视觉小说/运行序章界面检查")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("先停止播放");
        var story = AssetDatabase.LoadAssetAtPath<NovelStoryAsset>(NovelPrologueBuilder.StoryPath);
        if (story == null) throw new InvalidOperationException("请先生成序章剧情");
        visited = screenshots = 0;
        reachedSouvenir = false;
        branchPhase = 0;
        result = "";
        deadline = 0;
        SessionState.SetBool(Key, true);
        NovelStoryPreview.Play(story, null);
    }

    private static void Poll()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) return;
        if (deadline == 0)
        {
            deadline = EditorApplication.timeSinceStartup + 120;
            next = EditorApplication.timeSinceStartup + 1;
            result = "轮询启动\n";
            return;
        }
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + .02;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("界面检查超时，已访问 " + visited);
            var player = UnityEngine.Object.FindAnyObjectByType<NovelPlayer>();
            var view = UnityEngine.Object.FindAnyObjectByType<NovelPresentationController>();
            if (player?.Session == null || view == null) return;
            var session = player.Session;
            if (session.Status == NovelPlaybackStatus.Faulted) throw new InvalidOperationException(session.Error);
                        visited++;
            if (screenshots == 0 && visited == 8)
            {
                screenshots++;
                Directory.CreateDirectory(".codely-cli/test-results");
                ScreenCapture.CaptureScreenshot(".codely-cli/test-results/visual-novel-prologue-opening.png");
                result += "PASS 开场界面已截屏：" + session.Current.nodeId + "\n";
                if (string.IsNullOrEmpty(view.body.text)) throw new InvalidOperationException("正文为空");
                if (view.background.sprite == null) throw new InvalidOperationException("背景占位未加载");
            }
            bool onYesBranch = session.Status == NovelPlaybackStatus.Dialogue && (session.Current.text ?? "").StartsWith("我们的时间");
            if (onYesBranch && branchPhase == 0)
            {
                // Reveal the whole line first so the deferred screenshot is not taken mid-typewriter.
                session.CompleteText();
                branchPhase = 1;
                next = EditorApplication.timeSinceStartup + .2;
                return;
            }
            if (onYesBranch && branchPhase == 1)
            {
                Directory.CreateDirectory(".codely-cli/test-results");
                ScreenCapture.CaptureScreenshot(".codely-cli/test-results/prologue-branch-yes.png");
                result += "PASS 好分支正文：" + session.Current.text + "（说话人：" + session.FormatText(session.Story.FindCharacter(session.Current.characterId)?.displayName) + "）\n";
                branchPhase = 2;
                next = EditorApplication.timeSinceStartup + .4;
                return;
            }
            if (onYesBranch && branchPhase == 2)
            {
                Finish(true);
                return;
            }
            if (session.Status == NovelPlaybackStatus.TextInput)
            {
                if (!session.SubmitText("勒特")) throw new InvalidOperationException("名字输入失败");
                result += "PASS 名字输入界面可用\n";
            }
            else if (session.Status == NovelPlaybackStatus.Choice)
            {
                if (!reachedSouvenir)
                {
                    result += "PASS 选项：" + string.Join(" / ", session.Choices.Select(c => c.text).ToArray()) + "\n";
                    if (session.Choices.Any(c => c.text == "【什么都不拿】"))
                    {
                        reachedSouvenir = true;
                        if (session.Choices.Count != 4) throw new InvalidOperationException("信物选项数量错误：" + session.Choices.Count);
                        Canvas.ForceUpdateCanvases();
                        ScreenCapture.CaptureScreenshot(".codely-cli/test-results/visual-novel-prologue-souvenir.png");
                        result += "PASS 信物选项为4项（抽中2件+032+什么都不拿）\n";
                        result += "PASS 界面推进到信物选择，访问节点 " + visited + "\n";
                        Finish(true);
                        return;
                    }
                }
                session.Select(0);
            }
            else if (session.Status == NovelPlaybackStatus.Wait)
            {
                session.Tick(1000);
            }
            else
            {
                session.CompleteText();
                session.Continue();
            }
        }
        catch (Exception ex) { result += "FAIL " + ex + "\n"; Finish(false); }
    }

    private static void Finish(bool passed)
    {
        SessionState.SetBool(Key, false);
        Directory.CreateDirectory(".codely-cli/test-results");
        File.WriteAllText(".codely-cli/test-results/visual-novel-prologue-flow.txt", result);
        if (passed) Debug.Log("[VisualNovel] 序章界面检查通过\n" + result);
        else Debug.LogError("[VisualNovel] 序章界面检查失败\n" + result);
        EditorApplication.ExitPlaymode();
    }
}
