using System;
using System.Collections.Generic;
using System.Linq;
using Lament.VisualNovel;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class NovelPrologueFlowTests
{
    private static NovelStoryAsset Load() => AssetDatabase.LoadAssetAtPath<NovelStoryAsset>("Assets/VisualNovel/Stories/Prologue.asset");

    // Drives the pure state machine: no UI, no scene, deterministic and fast.
    private static (NovelSession session, int visited, int choices) Play(NovelStoryAsset story, string name, string souvenir)
    {
        var session = new NovelSession();
        session.Start(story);
        int visited = 0, choices = 0;
        var guard = 0;
        while (session.Status != NovelPlaybackStatus.Ended)
        {
            if (++guard > 5000) throw new InvalidOperationException("节点循环超过上限，当前：" + session.Current?.nodeId);
            if (session.Status == NovelPlaybackStatus.Faulted) throw new InvalidOperationException(session.Error);
            visited++;
            switch (session.Status)
            {
                case NovelPlaybackStatus.Dialogue:
                    session.CompleteText();
                    session.Continue();
                    break;
                case NovelPlaybackStatus.TextInput:
                    Assert.IsTrue(session.SubmitText(name), "名字提交失败");
                    break;
                case NovelPlaybackStatus.Choice:
                    choices++;
                    Assert.IsNotEmpty(session.Choices, "空选项：" + session.Current.nodeId);
                    int pick = 0;
                    if (souvenir != null)
                    {
                        pick = IndexOf(session.Choices, souvenir);
                        if (pick < 0) throw new InvalidOperationException("本次抽中项里没有：" + souvenir);
                    }
                    session.Select(pick);
                    break;
                case NovelPlaybackStatus.Wait:
                    session.Tick(1000);
                    break;
                default:
                    throw new InvalidOperationException("未处理状态：" + session.Status);
            }
        }
        return (session, visited, choices);
    }

    private static int IndexOf(IReadOnlyList<NovelChoice> list, string text)
    {
        for (int i = 0; i < list.Count; i++) if (list[i].text == text) return i;
        return -1;
    }

    [Test]
    public void FullScriptReachesEndWithChoicesAndName()
    {
        var story = Load();
        Assert.IsNotNull(story);
        var (session, visited, choices) = Play(story, "勒特", null);
        Assert.Greater(visited, 450, "访问节点过少，剧本可能中断");
        Assert.AreEqual(3, choices, "应出现记忆、承诺、信物三次选择");
        Assert.AreEqual("勒特", session.Variables.Get("name").text);
        Assert.IsTrue(session.State.sampledNodes.Count >= 1, "未记录抽样节点");
    }

    [Test]
    public void SouvenirChoiceShowsTwoSampledPlus032AndNone()
    {
        var story = Load();
        var session = new NovelSession();
        session.Start(story);
        var visited = 0;
        var guard = 0;
        while (session.Status != NovelPlaybackStatus.Choice || !session.Current.choices.Any(c => c.text == "【什么都不拿】"))
        {
            if (++guard > 5000) throw new InvalidOperationException("未到达信物选择");
            if (session.Status == NovelPlaybackStatus.Faulted) throw new InvalidOperationException(session.Error);
            if (session.Status == NovelPlaybackStatus.Dialogue) { session.CompleteText(); session.Continue(); }
            else if (session.Status == NovelPlaybackStatus.TextInput) session.SubmitText("勒特");
            else if (session.Status == NovelPlaybackStatus.Choice) session.Select(0);
            if (++visited > 4000) throw new InvalidOperationException("推进过久");
        }
        Assert.AreEqual(4, session.Choices.Count);
        // Two of the five body flags are true, and exactly the matching item options are offered.
        var flags = Enumerable.Range(1, 5).Count(i => session.Variables.Get("s" + i).boolean);
        Assert.AreEqual(2, flags, "应恰好抽中两具");
        Assert.IsTrue(session.Choices.Any(c => c.text == "【032号金属手环】"));
        Assert.IsTrue(session.Choices.Any(c => c.text == "【什么都不拿】"));
        // Selecting "什么都不拿" routes to the turn-away narration instead of the touch line.
        session.Select(IndexOf(session.Choices, "【什么都不拿】"));
        Assert.AreEqual("什么都不拿", session.Variables.Get("souvenir").text);
        while (session.Status == NovelPlaybackStatus.Dialogue) { session.CompleteText(); session.Continue(); }
        Assert.AreNotEqual(NovelPlaybackStatus.Faulted, session.Status);
    }

    [Test]
    public void SampledChoiceSurvivesSaveAndLoadWithoutReshuffling()
    {
        var story = Load();
        var session = new NovelSession();
        session.Start(story);
        var guard = 0;
        while (session.Status != NovelPlaybackStatus.Choice || !session.Current.choices.Any(c => c.text == "【什么都不拿】"))
        {
            if (++guard > 5000) throw new InvalidOperationException("未到达信物选择");
            if (session.Status == NovelPlaybackStatus.Dialogue) { session.CompleteText(); session.Continue(); }
            else if (session.Status == NovelPlaybackStatus.TextInput) session.SubmitText("勒特");
            else if (session.Status == NovelPlaybackStatus.Choice) session.Select(0);
        }
        var before = session.Choices.Select(c => c.text).ToList();
        var saved = session.Snapshot();
        session.Restore(story, saved);
        var after = session.Choices.Select(c => c.text).ToList();
        CollectionAssert.AreEqual(before, after, "读档后信物选项发生变化");
    }
}
