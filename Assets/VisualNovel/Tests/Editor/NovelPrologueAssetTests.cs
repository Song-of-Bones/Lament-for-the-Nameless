using System;
using System.Linq;
using Lament.VisualNovel;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class NovelPrologueAssetTests
{
    private static NovelStoryAsset Load() => AssetDatabase.LoadAssetAtPath<NovelStoryAsset>("Assets/VisualNovel/Stories/Prologue.asset");

    [Test] public void PrologueAssetValidatesWithoutErrors()
    {
        var story = Load();
        Assert.IsNotNull(story, "请先运行 工具/视觉小说/由剧本生成序章");
        Assert.IsEmpty(NovelStoryValidator.Validate(story));
    }
    [Test] public void PrologueKeepsFullScriptAndStructure()
    {
        var story = Load();
        Assert.IsNotNull(story);
        Assert.AreEqual("lament-prologue", story.storyId);
        Assert.GreaterOrEqual(story.nodes.Count(n => n.type == NovelNodeType.Dialogue), 500, "台词节点数量异常，疑似剧本被截断");
        var speakerSet = story.nodes.Select(n => n.characterId).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        foreach (var id in new[] { "勒特", "末戮", "未绥", "墨离", "月映知", "狼少", "军官", "未知人物", "游击队队员", "喀兹士兵" })
            Assert.Contains(id, speakerSet, "缺少角色台词：" + id);
        Assert.IsTrue(story.nodes.Any(n => n.type == NovelNodeType.TextInput && n.inputVariable == "name"));
        Assert.IsTrue(story.nodes.Any(n => n.type == NovelNodeType.SampleFlags && n.sampleCount == 2 && n.sampleVariables.Count == 5));
        var choice = story.nodes.First(n => n.choices.Count == 7);
        Assert.AreEqual(5, choice.choices.Count(c => c.conditional), "抽中的两件信物应带条件，032与什么都不拿无条件");
        Assert.IsTrue(story.nodes.Any(n => n.type == NovelNodeType.End));
    }
    [Test] public void SouvenirConditionRoutesToDifferentLeadIn()
    {
        var story = Load();
        Assert.IsNotNull(story);
        var conditional = story.nodes.First(n => n.type == NovelNodeType.Conditional && n.condition.variable == "souvenir");
        Assert.IsNotNull(conditional.whenTrue);
        StringAssert.Contains("转过身", conditional.whenTrue.text);
        Assert.IsNotNull(conditional.whenFalse);
        StringAssert.Contains("手指刚触碰到", conditional.whenFalse.text);
    }
    [Test] public void BlackPlaceholdersAreConfigured()
    {
        var story = Load();
        Assert.IsNotNull(story);
        var black = story.FindResource("black");
        Assert.IsNotNull(black?.image, "缺少纯黑占位图");
        Assert.IsTrue(story.nodes.Any(n => n.type == NovelNodeType.Command && n.command == NovelCommandType.Background && n.resourceId == "black"));
        foreach (var id in new[] { "勒特", "末戮", "未绥", "墨离", "月映知", "狼少" })
            Assert.IsNotNull(story.FindCharacter(id)?.portrait, "缺少纯黑立绘占位：" + id);
    }
    [Test] public void GeneratedTreeHasNoDeadEnds()
    {
        var story = Load();
        Assert.IsNotNull(story);
        var reached = new System.Collections.Generic.HashSet<NovelNodeAsset>();
        var stack = new System.Collections.Generic.Stack<NovelNodeAsset>();
        stack.Push(story.entryNode);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node == null || !reached.Add(node)) continue;
            foreach (var target in node.Targets()) stack.Push(target);
        }
        Assert.IsTrue(reached.Any(n => n.type == NovelNodeType.End), "入口无法走到结局");
        // Every non-end node must have an outgoing edge.
        foreach (var node in story.nodes.Where(n => n.type != NovelNodeType.End))
            Assert.IsTrue(node.Targets().Any(t => t != null), "存在断链节点：" + node.nodeId);
    }
}
