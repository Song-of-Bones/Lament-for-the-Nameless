using System.Linq;
using Lament.VisualNovel;
using UnityEditor;
using UnityEngine;

public static class NovelPrologueDiagnostics
{
    [MenuItem("工具/视觉小说/检查序章结构")]
    public static void Inspect()
    {
        var story = AssetDatabase.LoadAssetAtPath<NovelStoryAsset>("Assets/VisualNovel/Stories/Prologue.asset");
        if (story == null) { Debug.LogWarning("未生成序章"); return; }
        var unreachable = NovelStoryValidator.Unreachable(story);
        var errors = NovelStoryValidator.Validate(story);
        Debug.Log("[Prologue] 节点=" + story.nodes.Count + " 对话=" + story.nodes.Count(n => n.type == NovelNodeType.Dialogue) +
                  " 校验错误=" + errors.Count + " 不可达=" + unreachable.Count);

        Report(story, "我们的时间", 1);
        Report(story, "好吧，记住你的名字", 1);
        Report(story, "在将来，你会答应我们的", 1);
        Report(story, "从第一具尸体", 1);
        Report(story, "转过身，准备离开", 1);
        Report(story, "手指刚触碰到那个东西", 1);
        Report(story, "(不好)", 0);
        Report(story, "（不好）", 0);

        var yes = story.nodes.FirstOrDefault(n => (n.text ?? "").StartsWith("我们的时间"));
        var no = story.nodes.FirstOrDefault(n => (n.text ?? "").StartsWith("在将来，你会答应我们的"));
        Debug.Log("[Prologue] 好分支下一节点=" + (yes?.next?.text ?? "null") + " / 不好分支下一节点=" + (no?.next?.text ?? "null"));
        var bad = story.nodes.Where(n => n.type == NovelNodeType.Dialogue && (n.text ?? "").Contains("未知人物：")).ToList();
        Debug.Log("[Prologue] 残留说话人前缀的台词=" + bad.Count + (bad.Count > 0 ? "\n" + string.Join("\n", bad.Take(5).Select(n => n.nodeId + ": " + n.text)) : ""));
    }

    private static void Report(NovelStoryAsset story, string fragment, int expected)
    {
        var matches = story.nodes.Where(n => (n.text ?? "").Contains(fragment)).ToList();
        Debug.Log("[Prologue] \"" + fragment + "\" 命中 " + matches.Count + "（预期 " + expected + "）" +
                  (matches.Count > 0 ? " e.g. " + matches[0].nodeId + ": " + matches[0].text : ""));
    }
}
