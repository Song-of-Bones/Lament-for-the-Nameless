using System;
using System.Collections.Generic;
using System.IO;
using Lament.VisualNovel;
using UnityEditor;
using UnityEngine;

public static class NovelPrologueSampleSetup
{
    public const string Path = "Assets/视觉小说/剧情/序章抽样组件示例.asset";

    [MenuItem("工具/视觉小说/创建序章抽样组件示例")]
    public static void Create()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("先停止播放");
        if (AssetDatabase.LoadMainAssetAtPath(Path) != null) { Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(Path); return; }
        var story = ScriptableObject.CreateInstance<NovelStoryAsset>();
        story.storyId = "prologue-sampling-components"; story.displayName = "序章组件示例（非正式正文）";
        story.variables.Add(new NovelVariableDefinition { id = "name", initial = new NovelValue { type = NovelValueType.String, text = "勒特" } });
        story.variables.Add(new NovelVariableDefinition { id = "souvenir", initial = new NovelValue { type = NovelValueType.String } });
        AssetDatabase.CreateAsset(story, Path);
        var input = Node(story, "name", NovelNodeType.TextInput, "组件示例：请输入角色称呼（不修改正式剧本）"); input.inputVariable = "name";
        var sample = Node(story, "sample", NovelNodeType.SampleFlags); sample.sampleCount = 2; input.next = sample;
        var labels = new[] { "布制护身符", "骸骨吊坠", "战靴仿品", "哑铜铃", "研究笔记本" };
        var checks = new List<NovelNodeAsset>(); var lines = new List<NovelNodeAsset>();
        for (int i = 0; i < 5; i++)
        {
            string id = "inspect_" + (i + 1);
            sample.sampleVariables.Add(id);
            story.variables.Add(new NovelVariableDefinition { id = id, initial = new NovelValue { type = NovelValueType.Boolean } });
            var branch = Node(story, "check_" + (i + 1), NovelNodeType.Conditional);
            branch.condition = IsTrue(id); checks.Add(branch);
            lines.Add(Node(story, "body_" + (i + 1), NovelNodeType.Dialogue, "组件示例：这次出现的调查对象对应【" + labels[i] + "】。"));
            branch.whenTrue = lines[i];
        }
        var mandatory = Node(story, "body_6", NovelNodeType.Dialogue, "组件示例：第六具必定出现，对应032号金属手环。");
        sample.next = checks[0];
        for (int i = 0; i < 5; i++)
        {
            var next = i == 4 ? mandatory : checks[i + 1];
            checks[i].whenFalse = next; lines[i].next = next;
        }
        var choice = Node(story, "souvenir_choice", NovelNodeType.Choice, "只显示本局抽中的两件信物、032号手环及什么都不拿。");
        var final = Node(story, "result", NovelNodeType.Dialogue, "{name}选择了【{souvenir}】。本示例仅验证抽样与筛选，不替代正式序章。");
        var end = Node(story, "end", NovelNodeType.End); end.returnToLobby = false;
        mandatory.next = choice; final.next = end;
        for (int i = 0; i < 5; i++) choice.choices.Add(Choice(labels[i], final, "inspect_" + (i + 1)));
        choice.choices.Add(Choice("032号金属手环", final, null));
        choice.choices.Add(Choice("什么都不拿", final, null));
        story.entryNode = input;
        foreach (var node in story.nodes) EditorUtility.SetDirty(node);
        EditorUtility.SetDirty(story); AssetDatabase.SaveAssets();
        var errors = NovelStoryValidator.Validate(story);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
        Selection.activeObject = story;
        Debug.Log("[VisualNovel] 已创建序章抽样组件示例，正文与原始DOCX未改动");
    }

    private static NovelCondition IsTrue(string id) => new NovelCondition { variable = id, value = new NovelValue { type = NovelValueType.Boolean, boolean = true } };
    private static NovelChoice Choice(string label, NovelNodeAsset target, string flag) => new NovelChoice
    {
        text = label, target = target, conditional = flag != null, condition = flag == null ? new NovelCondition() : IsTrue(flag),
        operations = new List<NovelOperation> { new NovelOperation { variable = "souvenir", value = new NovelValue { type = NovelValueType.String, text = label } } }
    };
    private static NovelNodeAsset Node(NovelStoryAsset story, string id, NovelNodeType type, string text = "")
    {
        var node = ScriptableObject.CreateInstance<NovelNodeAsset>(); node.nodeId = node.name = id; node.type = type; node.text = text;
        story.nodes.Add(node); AssetDatabase.AddObjectToAsset(node, story); return node;
    }

    [MenuItem("工具/视觉小说/运行序章抽样逻辑测试")]
    public static void Test()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("先停止播放");
        var story = AssetDatabase.LoadAssetAtPath<NovelStoryAsset>(Path);
        if (story == null) throw new InvalidOperationException("请先创建序章抽样组件示例");
        for (int iteration = 0; iteration < 50; iteration++)
        {
            var session = new NovelSession(); session.Start(story);
            if (!session.SubmitText("测试角色")) throw new InvalidOperationException("姓名提交失败");
            int optional = 0; bool mandatory = false;
            while (session.Status == NovelPlaybackStatus.Dialogue)
            {
                if (session.Current.nodeId == "body_6") mandatory = true; else optional++;
                session.CompleteText(); session.Continue();
            }
            if (optional != 2 || !mandatory || session.Choices.Count != 4) throw new InvalidOperationException("调查数量或信物筛选错误");
            var saved = session.Snapshot();
            var names = new List<string>(); foreach (var choice in session.Choices) names.Add(choice.text);
            session.Restore(story, saved);
            for (int i = 0; i < names.Count; i++) if (session.Choices[i].text != names[i]) throw new InvalidOperationException("读档重抽或改写选项");
            session.Select(iteration % 4);
            if (session.Variables.Get("souvenir").text != names[iteration % 4]) throw new InvalidOperationException("信物写入错误");
        }
        Directory.CreateDirectory(".codely-cli/test-results");
        File.WriteAllText(".codely-cli/test-results/visual-novel-sampling.txt", "PASS 50次：前五抽二、必定第六、四个信物选项、读档固定、选择写入\n");
        Debug.Log("[VisualNovel] 序章抽样逻辑测试通过：50次完整流程");
    }
}
