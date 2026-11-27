using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Lament.VisualNovel;
using UnityEditor;
using UnityEngine;

public static class NovelPrologueBuilder
{
    public const string ScriptPath = "Assets/VisualNovel/Stories/PrologueScript.txt";
    public const string StoryPath = "Assets/VisualNovel/Stories/Prologue.asset";
    public const string BlackTexturePath = "Assets/VisualNovel/Art/PureBlack.png";
    private const string BlackId = "black";

    private sealed class PendingLink
    {
        public NovelNodeAsset node;
        public string target;
        public bool trueBranch;
    }

    [MenuItem("工具/视觉小说/由剧本生成序章")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先停止播放");
        // The manuscript source is edited outside Unity; force a reimport so generation never uses a stale cache.
        AssetDatabase.ImportAsset(ScriptPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        var source = AssetDatabase.LoadAssetAtPath<TextAsset>(ScriptPath);
        if (source == null) throw new InvalidOperationException("缺少剧本源文件：" + ScriptPath);
        EnsureBlackSprite();
        var story = LoadOrCreateStory();
        ClearGeneratedNodes(story);
        story.storyId = "lament-prologue";
        story.displayName = "序章";
        story.schemaVersion = 1;
        story.variables.Clear();
        story.characters.Clear();
        story.resources.Clear();

        AddVariable(story, "name", NovelValueType.String, text: "勒特");
        AddVariable(story, "memory", NovelValueType.Integer);
        AddVariable(story, "promise", NovelValueType.Boolean, boolean: true);
        AddVariable(story, "souvenir", NovelValueType.String);
        for (int i = 1; i <= 5; i++) AddVariable(story, "s" + i, NovelValueType.Boolean);

        var black = AssetDatabase.LoadAssetAtPath<Sprite>(BlackTexturePath);
        if (black == null) throw new InvalidOperationException("纯黑占位图导入失败");
        story.resources.Add(new NovelResourceDefinition { id = BlackId, image = black });
        AddCharacter(story, "勒特", "勒特", black);
        AddCharacter(story, "勒特？", "勒特？", black);
        AddCharacter(story, "末戮", "末戮", black);
        AddCharacter(story, "未绥", "未绥", black);
        AddCharacter(story, "墨离", "墨离", black);
        AddCharacter(story, "月映知", "月映知", black);
        AddCharacter(story, "狼少", "狼少", black);
        AddCharacter(story, "未知人物", "未知人物", null);
        AddCharacter(story, "军官", "军官", null);
        AddCharacter(story, "喀兹士兵", "喀兹士兵", null);
        AddCharacter(story, "游击队队员", "游击队队员", null);

        var lines = source.text.Replace("\r\n", "\n").Split('\n');
        var labels = new Dictionary<string, NovelNodeAsset>(StringComparer.Ordinal) { [""] = null };
        var unresolvedLabels = new List<string>();
        var pending = new List<PendingLink>();
        var choiceTargets = new Dictionary<(string node, string option), string>();
        NovelNodeAsset last = null;
        NovelNodeAsset pendingChoice = null;
        NovelNodeAsset first = null;
        string speaker = "";
        int sequence = 0;

        NovelNodeAsset Create(string id, NovelNodeType type)
        {
            var node = ScriptableObject.CreateInstance<NovelNodeAsset>();
            node.name = node.nodeId = id;
            node.type = type;
            AssetDatabase.AddObjectToAsset(node, story);
            story.nodes.Add(node);
            if (unresolvedLabels.Count > 0)
            {
                foreach (var label in unresolvedLabels) labels[label] = node;
                unresolvedLabels.Clear();
            }
            if (last != null)
            {
                if (last.type == NovelNodeType.Conditional && last.whenFalse == null) last.whenFalse = node;
                else if (last.next == null) last.next = node;
            }
            if (first == null) first = node;
            if (type != NovelNodeType.End) last = node;
            return node;
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("//")) continue;
            if (line.StartsWith("@label "))
            {
                var label = line.Substring(7).Trim();
                if (!labels.ContainsKey(label)) { labels[label] = null; unresolvedLabels.Add(label); }
                continue;
            }
            if (line.StartsWith("@speaker "))
            {
                speaker = line.Substring(9).Trim();
                if (speaker == "-") speaker = "";
                continue;
            }
            if (line.StartsWith("@goto "))
            {
                if (last == null) throw new InvalidOperationException("@goto 前没有可连接节点：" + line);
                pending.Add(new PendingLink { node = last, target = line.Substring(6).Trim() });
                last = null;
                continue;
            }
            if (line.StartsWith("@choice "))
            {
                var node = Create("c" + sequence++.ToString("D3"), NovelNodeType.Choice);
                node.text = line.Substring(8).Trim();
                pendingChoice = node;
                last = null;
                continue;
            }
            if (line.StartsWith("@sample "))
            {
                var parts = line.Substring(8).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var node = Create("sample" + sequence++.ToString("D3"), NovelNodeType.SampleFlags);
                node.sampleVariables = parts[0].Split(',').ToList();
                node.sampleCount = int.Parse(parts[1], CultureInfo.InvariantCulture);
                continue;
            }
            if (line.StartsWith("@input "))
            {
                var parts = line.Substring(7).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var node = Create("input" + sequence++.ToString("D3"), NovelNodeType.TextInput);
                node.inputVariable = parts[0];
                node.inputMaxLength = int.Parse(parts[1], CultureInfo.InvariantCulture);
                node.text = "（请输入你的名字）";
                continue;
            }
            if (line.StartsWith("@if "))
            {
                var match = Regex.Match(line, @"^@if\s+(\S+)\s*(==|!=|>=|<=|>|<)\s*(.+?)\s*->\s*(\S+)$");
                if (!match.Success) throw new InvalidOperationException("无法解析：" + line);
                var node = Create("if" + sequence++.ToString("D3"), NovelNodeType.Conditional);
                node.condition = new NovelCondition
                {
                    variable = match.Groups[1].Value,
                    comparison = Comparison(match.Groups[2].Value),
                    value = Value(story, match.Groups[1].Value, match.Groups[3].Value)
                };
                pending.Add(new PendingLink { node = node, target = match.Groups[4].Value, trueBranch = true });
                continue;
            }
            if (line.StartsWith("@black"))
            {
                var node = Create("bg" + sequence++.ToString("D3"), NovelNodeType.Command);
                node.command = NovelCommandType.Background;
                node.resourceId = BlackId;
                continue;
            }
            if (line.StartsWith("@effect "))
            {
                CreateEffect(line.Substring(8).Trim(), sequence++, (id, type) => Create(id, type));
                continue;
            }
            if (line.StartsWith("@end"))
            {
                Create("end", NovelNodeType.End).returnToLobby = true;
                continue;
            }
            if (line.StartsWith("- "))
            {
                if (pendingChoice == null) throw new InvalidOperationException("选项行不在 @choice 之后：" + line);
                var match = Regex.Match(line, @"^-\s*(.+?)\s*->\s*(\S+)(.*)$");
                if (!match.Success) throw new InvalidOperationException("无法解析选项：" + line);
                var choice = new NovelChoice { text = match.Groups[1].Value.Trim(), operations = new List<NovelOperation>() };
                foreach (Match tag in Regex.Matches(match.Groups[3].Value, @"\[(if|set)\s+([^\]]+)\]"))
                {
                    var payload = tag.Groups[2].Value.Trim();
                    if (tag.Groups[1].Value == "if")
                    {
                        choice.conditional = true;
                        choice.condition = new NovelCondition
                        {
                            variable = payload,
                            value = new NovelValue { type = NovelValueType.Boolean, boolean = true }
                        };
                    }
                    else
                    {
                        var eq = payload.IndexOf('=');
                        if (eq <= 0) throw new InvalidOperationException("set 语法错误：" + payload);
                        var name = payload.Substring(0, eq).Trim();
                        choice.operations.Add(new NovelOperation { variable = name, value = Value(story, name, payload.Substring(eq + 1).Trim()) });
                    }
                }
                pendingChoice.choices.Add(choice);
                choiceTargets[(pendingChoice.nodeId, choice.text)] = match.Groups[2].Value;
                continue;
            }
            var dialogue = Create("d" + sequence++.ToString("D4"), NovelNodeType.Dialogue);
            dialogue.text = line;
            dialogue.characterId = speaker;
        }

        foreach (var link in pending)
        {
            if (!labels.TryGetValue(link.target, out var target) || target == null)
                throw new InvalidOperationException("未解析的跳转标签：" + link.target);
            if (link.trueBranch) link.node.whenTrue = target;
            else if (link.node.type == NovelNodeType.Conditional) link.node.whenFalse = target;
            else link.node.next = target;
        }
        foreach (var node in story.nodes.Where(n => n.type == NovelNodeType.Choice))
            foreach (var choice in node.choices)
            {
                if (!choiceTargets.TryGetValue((node.nodeId, choice.text), out var label) || !labels.TryGetValue(label, out var target))
                    throw new InvalidOperationException("选项目标未解析：" + node.nodeId + " / " + choice.text);
                choice.target = target;
            }

        story.entryNode = first;
        EditorUtility.SetDirty(story);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        var errors = NovelStoryValidator.Validate(story);
        var unreachable = NovelStoryValidator.Unreachable(story);
        Debug.Log("[VisualNovel] 序章生成完成：节点 " + story.nodes.Count +
            "，对话 " + story.nodes.Count(n => n.type == NovelNodeType.Dialogue) +
            "，校验错误 " + errors.Count + "，不可达 " + unreachable.Count +
            (errors.Count > 0 ? "\n" + string.Join("\n", errors.Take(20)) : ""));
        Selection.activeObject = story;
    }

    private static NovelValue Value(NovelStoryAsset story, string name, string raw)
    {
        var definition = story.variables.Find(v => v.id == name);
        if (definition == null) throw new InvalidOperationException("使用了未声明变量：" + name);
        switch (definition.initial.type)
        {
            case NovelValueType.String: return new NovelValue { type = NovelValueType.String, text = raw };
            case NovelValueType.Boolean: return new NovelValue { type = NovelValueType.Boolean, boolean = bool.Parse(raw) };
            case NovelValueType.Integer: return new NovelValue { type = NovelValueType.Integer, integer = int.Parse(raw, CultureInfo.InvariantCulture) };
            default: return new NovelValue { type = NovelValueType.Number, number = float.Parse(raw, CultureInfo.InvariantCulture) };
        }
    }

    private static NovelComparison Comparison(string op)
    {
        switch (op)
        {
            case "==": return NovelComparison.Equal;
            case "!=": return NovelComparison.NotEqual;
            case ">": return NovelComparison.Greater;
            case "<": return NovelComparison.Less;
            case ">=": return NovelComparison.GreaterOrEqual;
            default: return NovelComparison.LessOrEqual;
        }
    }

    private static void AddVariable(NovelStoryAsset story, string id, NovelValueType type, string text = "", bool boolean = false)
        => story.variables.Add(new NovelVariableDefinition
        {
            id = id,
            initial = new NovelValue { type = type, text = text ?? "", boolean = boolean }
        });

    private static void AddCharacter(NovelStoryAsset story, string id, string displayName, Sprite portrait)
        => story.characters.Add(new NovelCharacterDefinition { id = id, displayName = displayName, nameColor = Color.white, portrait = portrait });

    private static NovelStoryAsset LoadOrCreateStory()
    {
        var story = AssetDatabase.LoadAssetAtPath<NovelStoryAsset>(StoryPath);
        if (story != null) return story;
        story = ScriptableObject.CreateInstance<NovelStoryAsset>();
        AssetDatabase.CreateAsset(story, StoryPath);
        return story;
    }

    private static void ClearGeneratedNodes(NovelStoryAsset story)
    {
        foreach (var node in story.nodes.Where(n => n != null).ToList())
        {
            AssetDatabase.RemoveObjectFromAsset(node);
            UnityEngine.Object.DestroyImmediate(node, true);
        }
        story.nodes.Clear();
        story.entryNode = null;
    }

    private static void EnsureBlackSprite()
    {
        if (AssetDatabase.LoadAssetAtPath<Sprite>(BlackTexturePath) != null) return;
        Directory.CreateDirectory("Assets/VisualNovel/Art");
        var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        texture.SetPixels(Enumerable.Repeat(Color.black, 16).ToArray());
        texture.Apply();
        File.WriteAllBytes(BlackTexturePath, texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(BlackTexturePath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(BlackTexturePath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();
    }

    private static void CreateEffect(string body, int index, Func<string, NovelNodeType, NovelNodeAsset> create)
    {
        var parts = body.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var node = create("fx" + index.ToString("D3"), NovelNodeType.Command);
        switch (parts[0])
        {
            case "flash":
                node.command = NovelCommandType.Flash;
                node.color = ParseColor(parts[1]);
                node.seconds = ParseFloat(parts[2]);
                break;
            case "shatter":
                node.command = NovelCommandType.Shatter;
                node.seconds = ParseFloat(parts[1]);
                break;
            case "shake":
                node.command = NovelCommandType.Shake;
                node.shakeStrength = ParseFloat(parts[1]);
                node.shakeFrequency = ParseFloat(parts[2]);
                node.seconds = ParseFloat(parts[3]);
                break;
            case "distortion":
                node.command = NovelCommandType.Distortion;
                node.color = ParseColor(parts[1]);
                node.seconds = ParseFloat(parts[2]);
                node.shakeStrength = 10;
                node.shakeFrequency = 3;
                break;
            case "fade":
                node.command = NovelCommandType.Fade;
                node.color = ParseColor(parts[1]);
                node.seconds = ParseFloat(parts[2]);
                node.waitForCompletion = true;
                break;
            default:
                throw new InvalidOperationException("未知效果：" + parts[0]);
        }
    }

    private static float ParseFloat(string raw) => float.Parse(raw, CultureInfo.InvariantCulture);

    private static Color ParseColor(string raw)
    {
        if (!ColorUtility.TryParseHtmlString(raw, out var color)) throw new InvalidOperationException("颜色格式错误：" + raw);
        return color;
    }
}
