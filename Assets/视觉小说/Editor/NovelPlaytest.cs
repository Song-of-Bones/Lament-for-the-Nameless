using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using UnityEngine.UI;
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
    private static NovelSaveData checkpoint;
    private static NovelSaveData routeSave;
    private static NovelSaveService saves;
    private static int pausedVisible;
    private static string result = "";

    static NovelPlaytest()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
            {
                step = 0;
                next = EditorApplication.timeSinceStartup + 2;
                deadline = next + 180;
                result = "";
                Application.runInBackground = true;
                saves = new NovelSaveService(Path.Combine(Application.dataPath, "../.codely-cli/test-results/playtest-saves"));
                EditorApplication.update += Update;
            }
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                EditorApplication.update -= Update;
                SessionState.SetBool(Key, false);
            }
        };
    }

    [MenuItem("工具/视觉小说/运行大厅到剧情集成测试")]
    public static void Run()
    {
        if (SceneManager.GetActiveScene().name != "游戏大厅" || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请在编辑模式打开大厅");
        SessionState.SetBool(Key, true);
        EditorApplication.EnterPlaymode();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        result += "PASS " + message + "\n";
    }

    private static void Update()
    {
        if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup < next) return;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("集成测试超时，步骤=" + step);
            var player = UnityEngine.Object.FindAnyObjectByType<NovelPlayer>();
            if (step == 0)
            {
                var entry = UnityEngine.Object.FindAnyObjectByType<NovelEntryPoint>();
                Check(entry != null, "大厅存在剧情入口");
                var button = entry.GetComponent<UIMenuButton>();
                button.进入选中();
                button.确认();
                step++;
                next = EditorApplication.timeSinceStartup + 2;
                return;
            }
            if ((step == 1 || step == 14) && (player == null || player.Session == null)) return;
            var view = UnityEngine.Object.FindAnyObjectByType<NovelPresentationController>();
            switch (step)
            {
                case 1:
                    Check(player.Session.Current.nodeId == "intro", "大厅确认后进入剧情首句");
                    Check(view.body.text.Contains("框架功能演示"), "正文和中文内容绑定");
                    Check(view.background != null && view.background.color.a > 0, "背景显示层已绑定");
                    view.ToggleAuto();
                    Check(player.AutoMode && !player.SkipMode, "自动按钮启用自动播放");
                    player.Session.CompleteText();
                    step++;
                    break;
                case 2:
                    if (player.Session.Current.nodeId == "intro") return;
                    Check(player.Session.Current.nodeId == "instructions", "自动播放实际推进至第二句");
                    view.ToggleAuto();
                    Check(!player.AutoMode, "自动按钮可关闭");
                    player.Session.CompleteText();
                    view.advance.onClick.Invoke();
                    step++;
                    break;
                case 3:
                    Check(player.Session.Status == NovelPlaybackStatus.Choice && player.Session.Choices.Count == 2, "二选一显示且阻止继续");
                    checkpoint = player.Session.Snapshot();
                    Canvas.ForceUpdateCanvases();
                    var choiceButton = view.choiceRoot.GetChild(1).GetComponent<Button>();
                    var choiceRect = (RectTransform)choiceButton.transform;
                    Check(choiceRect.rect.width > view.choiceRoot.rect.width * .9f && choiceRect.rect.height >= 70, "选项按钮占据有效横向点击区域");
                    var label = choiceButton.GetComponentInChildren<TMPro.TMP_Text>();
                    label.ForceMeshUpdate();
                    Check(label.textInfo.lineCount == 1, "选项文字正常横排且不挤成竖排");
                    Directory.CreateDirectory(".codely-cli/test-results");
                    ScreenCapture.CaptureScreenshot(".codely-cli/test-results/visual-novel-choices.png");
                    step = 17;
                    break;
                case 17:
                    var clickButton = view.choiceRoot.GetChild(1).GetComponent<Button>();
                    var clickRect = (RectTransform)clickButton.transform;
                    var pointer = new PointerEventData(EventSystem.current)
                    {
                        position = RectTransformUtility.WorldToScreenPoint(null, clickRect.TransformPoint(clickRect.rect.center)),
                        button = PointerEventData.InputButton.Left
                    };
                    var hits = new List<RaycastResult>();
                    EventSystem.current.RaycastAll(pointer, hits);
                    Check(hits.Count > 0 && ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject) == clickButton.gameObject, "鼠标射线首个命中为选项按钮");
                    ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
                    step = 4;
                    break;
                case 4:
                    Check(player.Session.Current.nodeId == "route-a", "选项 A 正确进入条件分支");
                    Check(player.Session.Variables.Get("route").integer == 1, "选项 A 变量写入");
                    Check(saves.TrySave(1, player.Session.Snapshot(), out var error), "隔离 JSON 文件写入：" + error);
                    Check(saves.TryLoad(1, out var saved, out error), "隔离 JSON 文件读取：" + error);
                    player.Resume(saved);
                    Check(player.Session.Current.nodeId == "route-a", "播放器读档恢复原节点");
                    view.OpenHistory();
                    Check(player.Suspended && view.historyText.text.Contains("路线 A"), "历史回看且暂停剧情");
                    view.ClosePanels();
                    view.OpenSlots();
                    Check(view.slotsPanel.activeSelf && player.Suspended, "存读档窗口暂停剧情");
                    view.ClosePanels();
                    player.Resume(checkpoint);
                    player.SelectChoice(1);
                    view.ToggleSkip();
                    step++;
                    break;
                case 5:
                    Check(player.Session.Current.nodeId == "route-b" && player.Session.Variables.Get("route").integer == 2, "选项 B 和变量正确恢复/分流");
                    Check(player.SkipMode && !player.AutoMode, "已读快进与自动互斥");
                    Check(!player.Session.CurrentWasRead, "新分支在进入前为未读");
                    player.Session.CompleteText();
                    step++;
                    break;
                case 6:
                    Check(player.Session.Current.nodeId == "route-b", "快进不会越过本次首次阅读的文本");
                    routeSave = player.Session.Snapshot();
                    player.Resume(routeSave);
                    Check(!player.AutoMode && !player.SkipMode, "读档重置播放模式");
                    view.ToggleSkip();
                    step++;
                    break;
                case 7:
                    if (player.Session.Current.nodeId == "route-b") return;
                    Check(player.Session.Current.nodeId == "final", "已读快进实际推进至下一句");
                    view.ToggleSkip();
                    player.Resume(routeSave);
                    var settings = UnityEngine.Object.FindAnyObjectByType<NovelSettingsAdapter>();
                    Check(settings != null, "剧情场景存在设置适配器");
                    player.SetAutoMode(true);
                    settings.OpenSettings();
                    Check(player.Suspended, "设置打开后立即暂停剧情");
                    pausedVisible = view.body.maxVisibleCharacters;
                    step++;
                    next = EditorApplication.timeSinceStartup + 2;
                    return;
                case 8:
                    Check(player.Session.Current.nodeId == "route-b" && player.Suspended, "设置打开期间自动模式不推进");
                    Check(view.body.maxVisibleCharacters == pausedVisible, "设置打开期间正文显示保持不变");
                    var window = UnityEngine.Object.FindAnyObjectByType<SettingsWindow>();
                    if (window == null) return;
                    player.SetAutoMode(false);
                    window.关闭();
                    step++;
                    break;
                case 9:
                    if (player.Suspended) return;
                    Check(player.Session.Current.nodeId == "route-b", "设置关闭后恢复当前剧情");
                    Check(!SceneManager.GetSceneByName("设置_异步窗口").isLoaded, "设置场景卸载完成");
                    Check(saves.TrySave(2, routeSave, out var saveError), "写入供继续游戏使用的隔离槽位：" + saveError);
                    Directory.CreateDirectory(".codely-cli/test-results");
                    ScreenCapture.CaptureScreenshot(".codely-cli/test-results/visual-novel-playtest.png");
                    step++;
                    break;
                case 10:
                    player.ReturnToLobby();
                    step = 13;
                    break;
                case 13:
                    if (SceneManager.GetActiveScene().name != "游戏大厅") return;
                    var resume = UnityEngine.Object.FindAnyObjectByType<NovelEntryPoint>();
                    Check(resume != null, "返回大厅后继续入口仍存在");
                    resume.ContinueFrom(saves);
                    step++;
                    break;
                case 14:
                    Check(player.Session.Current.nodeId == "route-b", "大厅继续入口恢复最近隔离存档节点");
                    Check(player.Session.Variables.Get("route").integer == 2, "跨场景继续恢复分支变量");
                    Check(player.Session.State.history.Count == routeSave.history.Count, "跨场景继续不会重复历史");
                    player.Session.CompleteText();
                    player.Continue();
                    step++;
                    break;
                case 15:
                    Check(player.Session.Current.nodeId == "final", "分支汇合至结束前对话");
                    player.Session.CompleteText();
                    player.Continue();
                    step++;
                    break;
                case 16:
                    if (SceneManager.GetActiveScene().name != "游戏大厅") return;
                    Check(true, "剧情结束返回大厅");
                    Finish(true);
                    return;
            }
            next = EditorApplication.timeSinceStartup + .3;
        }
        catch (Exception ex) { result += "FAIL " + ex + "\n"; Finish(false); }
    }

    private static void Finish(bool passed)
    {
        EditorApplication.update -= Update;
        SessionState.SetBool(Key, false);
        Directory.CreateDirectory(".codely-cli/test-results");
        File.WriteAllText(".codely-cli/test-results/visual-novel-playtest.txt", result);
        if (passed) Debug.Log("[VisualNovel] 集成测试全部通过\n" + result);
        else Debug.LogError("[VisualNovel] 集成测试失败\n" + result);
        EditorApplication.ExitPlaymode();
    }
}
