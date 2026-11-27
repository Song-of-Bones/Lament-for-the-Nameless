using System;
using System.IO;
using System.Linq;
using Lament.VisualNovel;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class NovelPlaytest
{
    private const string Key = "Lament.VisualNovel.Playtest";
    private static int step;
    private static double next;
    private static double deadline;
    private static string result = "";
    private static NovelSaveData saved;
    private static NovelSaveService saves;
    private static int returnAttempts;

    static NovelPlaytest()
    {
        // Polling avoids missing the one-shot play-mode event after a domain reload.
        EditorApplication.update += Poll;
    }

    [MenuItem("工具/视觉小说/运行大厅到序章集成测试")]
    public static void Run()
    {
        if (SceneManager.GetActiveScene().name != "Lobby" || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请在编辑模式打开大厅");
        step = 0;
        result = "";
        deadline = 0;
        SessionState.SetBool(Key, true);
        EditorApplication.EnterPlaymode();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        result += "PASS " + message + "\n";
    }

    private static void Poll()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) return;
        if (deadline == 0)
        {
            saves = new NovelSaveService(Path.Combine(Application.dataPath, "../.codely-cli/test-results/playtest-saves"));
            deadline = EditorApplication.timeSinceStartup + 180;
            next = EditorApplication.timeSinceStartup + 1.5;
            return;
        }
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + .25;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("集成测试超时，步骤=" + step);
            var player = UnityEngine.Object.FindAnyObjectByType<NovelPlayer>();
            var view = UnityEngine.Object.FindAnyObjectByType<NovelPresentationController>();
            if (step == 0)
            {
                var entry = UnityEngine.Object.FindAnyObjectByType<NovelEntryPoint>();
                Check(entry != null, "大厅存在剧情入口");
                var button = entry.GetComponent<UIMenuButton>();
                button.进入选中();
                button.确认();
                step = 1;
                next = EditorApplication.timeSinceStartup + 2;
                return;
            }
            if (step == 8)
            {
                // The VN scene (and its NovelPlayer) is destroyed during the load, so this
                // must run before the player guard below.
                if (!SceneManager.GetSceneByName("Lobby").isLoaded)
                {
                    if (returnAttempts < 3 && EditorApplication.timeSinceStartup >= next)
                    {
                        returnAttempts++;
                        result += "重试返回大厅 #" + returnAttempts + "\n";
                        var anyPlayer = UnityEngine.Object.FindAnyObjectByType<NovelPlayer>();
                        if (anyPlayer != null) anyPlayer.ReturnToLobby();
                        next = EditorApplication.timeSinceStartup + 3;
                    }
                    return;
                }
                var resumeEntry = UnityEngine.Object.FindAnyObjectByType<NovelEntryPoint>();
                Check(resumeEntry != null, "返回大厅后入口仍存在");
                resumeEntry.ContinueFrom(saves);
                step = 9;
                next = EditorApplication.timeSinceStartup + 2;
                return;
            }
            if (player?.Session == null || view == null) return;
            var session = player.Session;
            if (session.Status == NovelPlaybackStatus.Faulted) throw new InvalidOperationException(session.Error);
            switch (step)
            {
                case 1:
                    Check(session.Story.storyId == "lament-prologue", "大厅确认后加载序章剧情");
                    Check(SceneManager.GetActiveScene().name == "NovelRuntime", "进入独立剧情场景");
                    Check(!string.IsNullOrEmpty(view.body.text), "序章正文已显示：" + session.Current.nodeId);
                    step++;
                    break;
                case 2:
                    if (session.Status != NovelPlaybackStatus.TextInput) { Advance(session); return; }
                    Check(view.NameInput.Panel.activeSelf, "命名输入界面出现");
                    Check(session.SubmitText("勒特"), "提交名字成功");
                    step++;
                    break;
                case 3:
                    if (session.Status != NovelPlaybackStatus.Choice) { Advance(session); return; }
                    Check(session.Current.choices.Count == 2, "第一个选择为记忆二选一");
                    Check(session.Choices.All(c => !string.IsNullOrEmpty(c.text)), "选项文字非空");
                    session.Select(0);
                    step++;
                    break;
                case 4:
                    if (session.Status != NovelPlaybackStatus.Dialogue) { Advance(session); return; }
                    Check(session.Variables.Get("memory").integer == 1, "选择写入记忆变量");
                    saved = session.Snapshot();
                    Check(saves.TrySave(3, saved, out var error), "隔离存档写入：" + error);
                    view.OpenHistory();
                    Check(player.Suspended && view.historyText.text.Contains("死寂"), "历史包含已读开场且暂停剧情");
                    view.ClosePanels();
                    view.OpenSlots();
                    Check(view.slotsPanel.activeSelf && player.Suspended, "存读档窗口暂停剧情");
                    view.ClosePanels();
                    step++;
                    break;
                case 5:
                    player.Load(3);
                    Check(session.Current.nodeId == saved.nodeId, "读档恢复到存档节点");
                    Check(session.Variables.Get("memory").integer == 1, "读档恢复变量");
                    var adapter = UnityEngine.Object.FindAnyObjectByType<NovelSettingsAdapter>();
                    Check(adapter != null, "剧情场景存在设置适配器");
                    adapter.OpenSettings();
                    Check(player.Suspended, "设置打开后暂停剧情");
                    step++;
                    next = EditorApplication.timeSinceStartup + 1.5;
                    return;
                case 6:
                    var window = UnityEngine.Object.FindAnyObjectByType<SettingsWindow>();
                    if (window == null) return;
                    window.关闭();
                    step++;
                    break;
                case 7:
                    if (player.Suspended) return;
                    Check(session.Story.storyId == "lament-prologue", "关闭设置后继续原剧情");
                    Check(saves.TrySave(2, session.Snapshot(), out var e2), "写入继续游戏槽位：" + e2);
                    Directory.CreateDirectory(".codely-cli/test-results");
                    ScreenCapture.CaptureScreenshot(".codely-cli/test-results/visual-novel-prologue-playtest.png");
                    player.ReturnToLobby();
                    returnAttempts = 1;
                    step++;
                    break;
                case 9:
                    Check(session.Story.storyId == "lament-prologue", "继续游戏回到序章");
                    Check(session.Variables.Get("memory").integer == 1, "继续游戏恢复变量");
                    Check(session.State.history.Count == saved.history.Count, "继续游戏不重复历史");
                    Finish(true);
                    return;
            }
        }
        catch (Exception ex) { result += "FAIL " + ex + "\n"; Finish(false); }
    }

    private static void Advance(NovelSession session)
    {
        if (session.Status == NovelPlaybackStatus.Dialogue) { session.CompleteText(); session.Continue(); }
        else if (session.Status == NovelPlaybackStatus.Wait) session.Tick(1000);
        else if (session.Status == NovelPlaybackStatus.TextInput) session.SubmitText("勒特");
        else if (session.Status == NovelPlaybackStatus.Choice) session.Select(0);
    }

    private static void Finish(bool passed)
    {
        SessionState.SetBool(Key, false);
        Directory.CreateDirectory(".codely-cli/test-results");
        File.WriteAllText(".codely-cli/test-results/visual-novel-playtest.txt", result);
        if (passed) Debug.Log("[VisualNovel] 大厅到序章集成测试通过\n" + result);
        else Debug.LogError("[VisualNovel] 大厅到序章集成测试失败\n" + result);
        EditorApplication.ExitPlaymode();
    }
}
