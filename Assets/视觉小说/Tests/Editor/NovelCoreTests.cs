using System;
using System.Collections.Generic;
using System.IO;
using Lament.VisualNovel;
using NUnit.Framework;
using UnityEngine;

public sealed class NovelCoreTests
{
    private readonly List<UnityEngine.Object> allocated = new List<UnityEngine.Object>();
    private NovelStoryAsset story;
    private NovelNodeAsset first;
    private NovelNodeAsset choice;
    private NovelNodeAsset branch;
    private NovelNodeAsset a;
    private NovelNodeAsset b;
    private string directory;

    [SetUp]
    public void Setup()
    {
        story = Make<NovelStoryAsset>(); story.storyId = "test"; story.schemaVersion = 1;
        story.variables.Add(new NovelVariableDefinition { id = "route", initial = Int(0) });
        first = Node("first", NovelNodeType.Dialogue); first.text = "测试文本";
        choice = Node("choice", NovelNodeType.Choice);
        branch = Node("branch", NovelNodeType.Conditional);
        a = Node("a", NovelNodeType.Dialogue); a.text = "A";
        b = Node("b", NovelNodeType.Dialogue); b.text = "B";
        var end = Node("end", NovelNodeType.End);
        story.entryNode = first; first.next = choice;
        choice.choices.Add(new NovelChoice { text = "A", target = branch, operations = new List<NovelOperation> { new NovelOperation { variable = "route", value = Int(1) } } });
        choice.choices.Add(new NovelChoice { text = "B", target = branch, operations = new List<NovelOperation> { new NovelOperation { variable = "route", value = Int(2) } } });
        branch.condition = new NovelCondition { variable = "route", value = Int(1) };
        branch.whenTrue = a; branch.whenFalse = b; a.next = b.next = end;
        directory = Path.Combine(Path.GetTempPath(), "Lament-VN-Tests-" + Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void Cleanup()
    {
        foreach (var item in allocated) UnityEngine.Object.DestroyImmediate(item);
        allocated.Clear();
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    private T Make<T>() where T : ScriptableObject { var item = ScriptableObject.CreateInstance<T>(); allocated.Add(item); return item; }
    private NovelNodeAsset Node(string id, NovelNodeType type) { var n = Make<NovelNodeAsset>(); n.nodeId = id; n.type = type; story.nodes.Add(n); return n; }
    private static NovelValue Int(int value) => new NovelValue { type = NovelValueType.Integer, integer = value };

    [Test] public void LuaCheckpointBlocksAdvanceAndResumesOnlyOnCompletion()
    {
        var script = new TextAsset("fx.wait(1)"); allocated.Add(script);
        var node = Node("lua", NovelNodeType.LuaEffect); node.luaScript = script; node.next = first; story.entryNode = node;
        var session = new NovelSession(); session.Start(story);
        session.Continue(); session.Tick(100); Assert.AreEqual(NovelPlaybackStatus.LuaEffect, session.Status);
        var saved = session.Snapshot(); session.Restore(story, saved);
        Assert.AreEqual(NovelPlaybackStatus.LuaEffect, session.Status);
        session.CompleteLuaEffect(); Assert.AreSame(first, session.Current);
        session.Start(story); session.FailLuaEffect("script failed");
        Assert.AreEqual(NovelPlaybackStatus.Faulted, session.Status);
    }
    [Test] public void MissingLuaSourceIsRejected()
    {
        var node = Node("lua", NovelNodeType.LuaEffect); node.next = first;
        Assert.IsNotEmpty(NovelStoryValidator.Validate(story));
    }
    [Test] public void BlockingTransformRestoresFinalPoseWithoutReplaying()
    {
        var move = Node("move", NovelNodeType.Command);
        move.command = NovelCommandType.PortraitTransform; move.seconds = 2; move.waitForCompletion = true;
        move.pose.position = new Vector2(100, 20); move.pose.alpha = .4f; move.next = first; story.entryNode = move;
        int commands = 0; var session = new NovelSession(); session.Command += _ => commands++;
        session.Start(story); Assert.AreEqual(NovelPlaybackStatus.Wait, session.Status);
        session.Continue(); Assert.AreEqual("move", session.Current.nodeId);
        session.Tick(.5f); var saved = session.Snapshot();
        session.Restore(story, saved);
        Assert.AreEqual(new Vector2(100, 20), session.State.presentation.poses[1].position);
        Assert.AreEqual(.4f, session.State.presentation.poses[1].alpha);
        Assert.AreEqual(1, commands);
        session.Tick(1.5f); Assert.AreSame(first, session.Current);
    }
    [Test] public void OldSaveMissingPosesGetsIdentityDefaults()
    {
        var session = new NovelSession(); session.Start(story); var saved = session.Snapshot();
        saved.presentation.poses = null; session.Restore(story, saved);
        Assert.AreEqual(Vector2.one, session.State.presentation.poses[0].scale);
        Assert.AreEqual(1, session.State.presentation.poses[0].alpha);
    }
    [Test] public void InvalidTransformSlotIsRejected()
    {
        var move = Node("move", NovelNodeType.Command); move.command = NovelCommandType.PortraitTransform;
        move.portraitSlot = 8; move.next = first;
        Assert.IsNotEmpty(NovelStoryValidator.Validate(story));
    }
    [Test] public void ExpressionResolvesToSavedResourceAndPreservesPose()
    {
        var texture = new Texture2D(2, 2); allocated.Add(texture);
        var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * .5f); allocated.Add(sprite);
        story.resources.Add(new NovelResourceDefinition { id = "smile-image", image = sprite });
        var hero = new NovelCharacterDefinition { id = "hero" };
        hero.expressions.Add(new NovelExpressionDefinition { id = "smile", resourceId = "smile-image" }); story.characters.Add(hero);
        var expression = Node("expression", NovelNodeType.Command); expression.command = NovelCommandType.PortraitExpression;
        expression.characterId = "hero"; expression.expressionId = "smile"; expression.next = first;
        var move = Node("move", NovelNodeType.Command); move.command = NovelCommandType.PortraitTransform;
        move.pose.position = new Vector2(42, 18); move.next = expression; story.entryNode = move;
        var session = new NovelSession(); session.Start(story);
        Assert.AreEqual("smile-image", session.State.presentation.portraits[1]);
        var saved = session.Snapshot(); session.Restore(story, saved);
        Assert.AreEqual(new Vector2(42, 18), session.State.presentation.poses[1].position);
        Assert.AreEqual("smile-image", session.State.presentation.portraits[1]);
    }
    [Test] public void UnknownExpressionIsRejected()
    {
        var expression = Node("expression", NovelNodeType.Command); expression.command = NovelCommandType.PortraitExpression;
        expression.characterId = "missing"; expression.expressionId = "smile"; expression.next = first;
        Assert.IsNotEmpty(NovelStoryValidator.Validate(story));
    }
    [Test] public void NameInputBlocksAdvanceAndPersistsFormattedHistory()
    {
        story.variables.Add(new NovelVariableDefinition { id = "name", initial = new NovelValue { type = NovelValueType.String, text = "勒特" } });
        var input = Node("name-input", NovelNodeType.TextInput); input.inputVariable = "name"; input.inputMaxLength = 4;
        input.next = first; story.entryNode = input; first.text = "你好，{name}";
        var session = new NovelSession(); session.Start(story);
        session.Continue(); session.Tick(100); Assert.AreEqual(NovelPlaybackStatus.TextInput, session.Status);
        var saved = session.Snapshot(); session.Restore(story, saved);
        Assert.IsFalse(session.SubmitText(" ")); Assert.IsFalse(session.SubmitText("<b>x")); Assert.IsFalse(session.SubmitText("一二三四五"));
        Assert.IsTrue(session.SubmitText(" 阿明 "));
        Assert.AreEqual("你好，阿明", session.State.history[0].text);
        Assert.AreEqual("阿明", session.Variables.Get("name").text);
        session.Restore(story, session.Snapshot()); Assert.AreEqual("你好，阿明", session.FormatText(first.text));
    }
    [Test] public void NameInputRequiresStringVariable()
    {
        var input = Node("name-input", NovelNodeType.TextInput); input.inputVariable = "route"; input.next = first;
        Assert.IsNotEmpty(NovelStoryValidator.Validate(story));
    }
    [Test] public void BackgroundPoseAndVolumePersistWithOldDefaults()
    {
        var move = Node("camera", NovelNodeType.Command); move.command = NovelCommandType.BackgroundTransform;
        move.pose.position = new Vector2(200, -50); move.pose.scale = Vector2.one * 1.5f;
        var volume = Node("volume", NovelNodeType.Command); volume.command = NovelCommandType.AudioVolume;
        volume.audioBus = NovelAudioBus.Ambient; volume.volume = .25f; move.next = volume; volume.next = first; story.entryNode = move;
        var session = new NovelSession(); session.Start(story); var saved = session.Snapshot(); session.Restore(story, saved);
        Assert.AreEqual(move.pose.position, session.State.presentation.backgroundPose.position);
        Assert.AreEqual(.25f, session.State.presentation.volumes[1]);
        saved.presentation.volumes = null; saved.presentation.backgroundPose = null; session.Restore(story, saved);
        Assert.AreEqual(1, session.State.presentation.volumes[1]); Assert.AreEqual(Vector2.one, session.State.presentation.backgroundPose.scale);
    }
    [Test] public void InvalidAudioVolumeIsRejected()
    {
        var volume = Node("volume", NovelNodeType.Command); volume.command = NovelCommandType.AudioVolume;
        volume.volume = float.NaN; volume.next = first;
        Assert.IsNotEmpty(NovelStoryValidator.Validate(story));
    }
    [Test] public void SixActorStageRestoresLegacyThreeSlotSaves()
    {
        var session = new NovelSession(); session.Start(story);
        var saved = session.Snapshot(); saved.presentation.portraits = new[] { "left", "middle", "right" };
        saved.presentation.poses = new[] { new NovelPortraitPose { position = new Vector2(13, 14) }, null, null };
        session.Restore(story, saved);
        Assert.AreEqual(6, session.State.presentation.portraits.Length);
        Assert.AreEqual("left", session.State.presentation.portraits[0]);
        Assert.AreEqual(new Vector2(13, 14), session.State.presentation.poses[0].position);
        Assert.AreEqual(Vector2.one, session.State.presentation.poses[5].scale);
    }
    [Test] public void SampleFlagsSelectsTwoAndStaysFixedAcrossLoopAndRestore()
    {
        var sample = Node("sample", NovelNodeType.SampleFlags); sample.sampleCount = 2; sample.next = first;
        for (int i = 0; i < 5; i++)
        {
            string id = "body" + i; sample.sampleVariables.Add(id);
            story.variables.Add(new NovelVariableDefinition { id = id, initial = new NovelValue { type = NovelValueType.Boolean } });
        }
        story.entryNode = sample; first.next = sample;
        var session = new NovelSession(); session.Start(story);
        int count = 0; var flags = new bool[5];
        for (int i = 0; i < 5; i++) { flags[i] = session.Variables.Get("body" + i).boolean; if (flags[i]) count++; }
        Assert.AreEqual(2, count);
        var saved = session.Snapshot(); session.Restore(story, saved);
        session.Continue(); session.Continue();
        for (int i = 0; i < 5; i++) Assert.AreEqual(flags[i], session.Variables.Get("body" + i).boolean);
        Assert.AreEqual(1, session.State.sampledNodes.Count);
    }
    [Test] public void SampleFlagsRejectsDuplicatesAndInvalidCount()
    {
        var sample = Node("sample", NovelNodeType.SampleFlags); sample.sampleVariables.Add("route"); sample.sampleVariables.Add("route");
        sample.sampleCount = 3; sample.next = first;
        Assert.IsNotEmpty(NovelStoryValidator.Validate(story));
    }
    [Test] public void ValidStoryHasNoDiagnostics() => Assert.IsEmpty(NovelStoryValidator.Validate(story));
    [Test] public void DuplicateIdsAndMissingLinksAreRejected()
    {
        a.nodeId = first.nodeId; b.next = null;
        Assert.GreaterOrEqual(NovelStoryValidator.Validate(story).Count, 2);
    }
    [TestCase(0, "a", 1)] [TestCase(1, "b", 2)]
    public void ChoiceMutatesVariableAndBranches(int index, string target, int value)
    {
        var session = new NovelSession(); session.Start(story);
        session.Continue(); Assert.IsTrue(session.State.textComplete);
        session.Continue(); Assert.AreEqual(NovelPlaybackStatus.Choice, session.Status);
        session.Continue(); Assert.AreEqual("choice", session.Current.nodeId);
        session.Select(index);
        Assert.AreEqual(target, session.Current.nodeId);
        Assert.AreEqual(value, session.Variables.Get("route").integer);
    }
    [Test] public void TypeMismatchDoesNotChangeVariable()
    {
        var store = new NovelVariableStore(story.variables);
        Assert.Throws<InvalidOperationException>(() => store.Apply(new NovelOperation { variable = "route", value = new NovelValue { type = NovelValueType.String } }));
        Assert.AreEqual(0, store.Get("route").integer);
    }
    [Test] public void SavedValuesAreIndependentOfStoryDefaults()
    {
        var session = new NovelSession(); session.Start(story);
        session.Variables.Apply(new NovelOperation { variable = "route", operation = NovelOperationType.Add, value = Int(3) });
        Assert.AreEqual(0, story.variables[0].initial.integer);
        var copy = session.Snapshot(); copy.variables[0].initial.integer = 8;
        Assert.AreEqual(3, session.Variables.Get("route").integer);
    }
    [Test] public void ResumeDoesNotRepeatHistoryOrVariableOperations()
    {
        var session = new NovelSession(); session.Start(story); session.Continue(); session.Continue(); session.Select(0);
        var saved = session.Snapshot(); var resumed = new NovelSession(); resumed.Restore(story, saved);
        Assert.AreEqual("a", resumed.Current.nodeId); Assert.AreEqual(1, resumed.Variables.Get("route").integer);
        Assert.AreEqual(saved.history.Count, resumed.State.history.Count);
    }
    [Test] public void WrongStoryVersionIsRejected()
    {
        var session = new NovelSession(); session.Start(story); var saved = session.Snapshot(); saved.storyVersion++;
        Assert.Throws<InvalidOperationException>(() => session.Restore(story, saved));
        Assert.AreEqual("first", session.Current.nodeId);
    }
    [Test] public void SaveRoundTripAndOverwriteAreSupported()
    {
        var session = new NovelSession(); session.Start(story); var saves = new NovelSaveService(directory);
        Assert.IsTrue(saves.TrySave(1, session.Snapshot(), out var error), error);
        Assert.IsTrue(saves.TrySave(1, session.Snapshot(), out error), error);
        Assert.IsTrue(saves.TryLoad(1, out var saved, out error), error);
        Assert.AreEqual("first", saved.nodeId); Assert.AreEqual(1, saves.LatestSlot());
        Assert.IsTrue(File.Exists(Path.Combine(directory, "slot-1.json.bak")));
    }
    [Test] public void InvalidJsonAndUnknownSaveVersionAreHandled()
    {
        Directory.CreateDirectory(directory); var path = Path.Combine(directory, "slot-1.json");
        File.WriteAllText(path, "not json"); var saves = new NovelSaveService(directory);
        Assert.IsFalse(saves.TryLoad(1, out _, out _));
        File.WriteAllText(path, "{\"version\":99}"); Assert.IsFalse(saves.TryLoad(1, out _, out _));
    }
    [Test] public void ImmediateCyclesFailWithoutHanging()
    {
        var loop = Node("loop", NovelNodeType.SetVariable); loop.next = loop; story.entryNode = loop;
        var session = new NovelSession(); session.Start(story);
        Assert.AreEqual(NovelPlaybackStatus.Faulted, session.Status);
    }
    [Test] public void InvalidChoiceIndexDoesNotAdvance()
    {
        var session = new NovelSession(); session.Start(story); session.Continue(); session.Continue(); session.Select(-1); session.Select(99);
        Assert.AreEqual("choice", session.Current.nodeId);
    }
    [Test] public void WaitSnapshotRestoresRemainingTime()
    {
        var wait = Node("wait", NovelNodeType.Wait); wait.seconds = 5; wait.next = first; story.entryNode = wait;
        var session = new NovelSession(); session.Start(story); session.Tick(2);
        var resumed = new NovelSession(); resumed.Restore(story, session.Snapshot()); resumed.Tick(2);
        Assert.AreEqual("wait", resumed.Current.nodeId); resumed.Tick(1); Assert.AreEqual("first", resumed.Current.nodeId);
    }
    [Test] public void AutoWaitsForTextVoiceAndFullDelay()
    {
        var session = new NovelSession(); session.Start(story);
        var clock = new NovelPlaybackClock(); clock.SetAutoMode(true);
        clock.Tick(session, 10, false, false);
        Assert.AreEqual("first", session.Current.nodeId);
        session.CompleteText();
        clock.Tick(session, 10, false, true);
        Assert.AreEqual("first", session.Current.nodeId);
        clock.Tick(session, 1, false, false);
        Assert.AreEqual("first", session.Current.nodeId);
        clock.Tick(session, .6f, false, false);
        Assert.AreEqual("choice", session.Current.nodeId);
        clock.Tick(session, 20, false, false);
        Assert.AreEqual("choice", session.Current.nodeId);
    }
    [Test] public void SuspensionFreezesWaitAndAutoAdvance()
    {
        var session = new NovelSession(); session.Start(story); session.CompleteText();
        var clock = new NovelPlaybackClock(); clock.SetAutoMode(true);
        clock.Tick(session, 20, true, false);
        Assert.AreEqual("first", session.Current.nodeId);
        clock.Tick(session, 1, false, false);
        Assert.AreEqual("first", session.Current.nodeId);
        var wait = Node("wait", NovelNodeType.Wait); wait.seconds = 5; wait.next = first;
        session.Start(story, "wait"); clock.Tick(session, 20, true, false);
        Assert.AreEqual(5, session.State.waitRemaining);
    }
    [Test] public void SkipStopsAtUnreadTextAndChoices()
    {
        var session = new NovelSession(); session.Start(story);
        var clock = new NovelPlaybackClock(); clock.SetSkipMode(true);
        clock.Tick(session, 20, false, false);
        Assert.IsFalse(session.State.textComplete);
        session.CompleteText();
        clock.Tick(session, 20, false, false);
        Assert.AreEqual("first", session.Current.nodeId);
        var saved = session.Snapshot(); session.Restore(story, saved);
        clock.Tick(session, .1f, false, false);
        Assert.AreEqual("choice", session.Current.nodeId);
        clock.Tick(session, 20, false, false);
        Assert.AreEqual("choice", session.Current.nodeId);
    }
    [Test] public void PlaybackModesAreExclusiveAndResettable()
    {
        var clock = new NovelPlaybackClock(); clock.SetAutoMode(true); clock.SetSkipMode(true);
        Assert.IsTrue(clock.SkipMode); Assert.IsFalse(clock.AutoMode);
        clock.SetAutoMode(true); Assert.IsTrue(clock.AutoMode); Assert.IsFalse(clock.SkipMode);
        clock.Reset(); Assert.IsFalse(clock.AutoMode); Assert.IsFalse(clock.SkipMode);
    }
    [Test] public void NodePreviewUsesDefaultsAndDoesNotRewriteEntry()
    {
        var entry = story.entryNode;
        var session = new NovelSession(); session.Start(story, "branch");
        Assert.AreEqual("b", session.Current.nodeId);
        Assert.AreEqual(0, session.Variables.Get("route").integer);
        Assert.AreSame(entry, story.entryNode);
        Assert.AreEqual(1, session.State.history.Count);
    }
    [Test] public void InvalidPreviewDoesNotReplaceLiveSession()
    {
        var session = new NovelSession(); session.Start(story);
        Assert.Throws<InvalidOperationException>(() => session.Start(story, "missing"));
        Assert.AreSame(first, session.Current);
    }
    [Test] public void PresentationSnapshotSurvivesRestoreWithoutRepeatingCommands()
    {
        var fade = Node("fade", NovelNodeType.Command); fade.command = NovelCommandType.Fade;
        fade.color = new Color(.2f, .3f, .4f, .5f); fade.next = first; story.entryNode = fade;
        int calls = 0;
        var session = new NovelSession(); session.Command += _ => calls++;
        session.Start(story);
        var saved = session.Snapshot(); session.Restore(story, saved);
        Assert.AreEqual(1, calls);
        Assert.AreEqual(fade.color, session.State.presentation.overlay);
        saved.presentation.portraits[0] = "mutated-copy";
        Assert.AreEqual("", session.State.presentation.portraits[0]);
    }
    [Test] public void InvalidRestoredChoicePreservesLiveState()
    {
        var session = new NovelSession(); session.Start(story);
        var saved = session.Snapshot(); saved.nodeId = "choice";
        foreach (var item in choice.choices) { item.conditional = true; item.condition = new NovelCondition { variable = "route", value = Int(99) }; }
        Assert.Throws<InvalidOperationException>(() => session.Restore(story, saved));
        Assert.AreSame(first, session.Current);
    }
    [Test] public void NoVisibleChoicesProducesFault()
    {
        foreach (var item in choice.choices) { item.conditional = true; item.condition = new NovelCondition { variable = "route", value = Int(99) }; }
        var session = new NovelSession(); session.Start(story); session.Continue(); session.Continue();
        Assert.AreEqual(NovelPlaybackStatus.Faulted, session.Status);
    }
}
