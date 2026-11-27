using System;
using System.Collections.Generic;
using System.Linq;

namespace Lament.VisualNovel
{
    public static class NovelStoryValidator
    {
        public static List<string> Validate(NovelStoryAsset story)
        {
            var errors = new List<string>();
            if (story == null) { errors.Add("未指定剧情"); return errors; }
            if (string.IsNullOrWhiteSpace(story.storyId)) errors.Add("storyId 为空");
            if (story.schemaVersion < 1) errors.Add("剧情版本必须大于 0");
            var nodes = new HashSet<NovelNodeAsset>(story.nodes.Where(n => n != null));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (story.entryNode == null || !nodes.Contains(story.entryNode)) errors.Add("入口节点缺失或未加入剧情");
            var variables = new Dictionary<string, NovelValueType>(StringComparer.Ordinal);
            foreach (var variable in story.variables)
            {
                if (variable == null || string.IsNullOrWhiteSpace(variable.id) || variable.initial == null) { errors.Add("无效变量定义"); continue; }
                if (variables.ContainsKey(variable.id)) errors.Add("重复变量：" + variable.id);
                else variables.Add(variable.id, variable.initial.type);
            }
            CheckIds(story.characters.Select(c => c?.id), "角色", errors);
            CheckIds(story.resources.Select(r => r?.id), "资源", errors);
            foreach (var character in story.characters)
            {
                if (character?.expressions == null) continue;
                CheckIds(character.expressions.Select(e => e?.id), "差分 " + character.id, errors);
                foreach (var expression in character.expressions)
                    if (expression != null && story.FindResource(expression.resourceId)?.image == null) errors.Add("差分缺少图片资源：" + character.id + "/" + expression.id);
            }
            foreach (var node in story.nodes)
            {
                if (node == null) { errors.Add("节点列表含空引用"); continue; }
                string prefix = "[" + node.nodeId + "] ";
                if (string.IsNullOrWhiteSpace(node.nodeId) || !ids.Add(node.nodeId)) errors.Add(prefix + "空或重复节点 ID");
                foreach (var target in node.Targets())
                    if (target == null || !nodes.Contains(target)) errors.Add(prefix + "目标节点断链或不属于本剧情");
                if (node.type == NovelNodeType.Dialogue)
                {
                    if (string.IsNullOrWhiteSpace(node.text)) errors.Add(prefix + "对话文本为空");
                    if (!string.IsNullOrEmpty(node.characterId) && story.FindCharacter(node.characterId) == null) errors.Add(prefix + "未知角色");
                }
                if (node.type == NovelNodeType.LuaEffect)
                {
                    if (node.luaScript == null || string.IsNullOrWhiteSpace(node.luaScript.text) || node.luaScript.text.Length > NovelLuaEffect.MaxSourceLength) errors.Add(prefix + "缺少Lua脚本或超过64K字符");
                    if (!Finite(node.luaTimeout) || node.luaTimeout < .1f || node.luaTimeout > 120) errors.Add(prefix + "Lua时限必须为0.1～120秒");
                }
                if (node.type == NovelNodeType.SampleFlags)
                {
                    if (node.sampleVariables == null || node.sampleVariables.Count == 0 || node.sampleCount < 0 || node.sampleCount > node.sampleVariables.Count) errors.Add(prefix + "抽样数量无效");
                    else
                    {
                        var sampleIds = new HashSet<string>();
                        foreach (var variable in node.sampleVariables)
                            if (string.IsNullOrEmpty(variable) || !sampleIds.Add(variable) || !variables.TryGetValue(variable, out var sampleType) || sampleType != NovelValueType.Boolean) errors.Add(prefix + "抽样标记必须为不重复的已声明布尔变量");
                    }
                }
                if (node.type == NovelNodeType.TextInput)
                {
                    if (string.IsNullOrWhiteSpace(node.inputVariable) || !variables.TryGetValue(node.inputVariable, out var inputType) || inputType != NovelValueType.String) errors.Add(prefix + "输入节点必须绑定已声明的字符串变量");
                    if (node.inputMaxLength < 1 || node.inputMaxLength > 64) errors.Add(prefix + "输入长度应为1～64");
                }
                if (node.type == NovelNodeType.Conditional) CheckCondition(node.condition, variables, errors, prefix);
                if (node.type == NovelNodeType.SetVariable) CheckOperations(node.operations, variables, errors, prefix);
                if (node.type == NovelNodeType.Wait && (float.IsNaN(node.seconds) || float.IsInfinity(node.seconds) || node.seconds < 0)) errors.Add(prefix + "等待时间无效");
                if (node.type == NovelNodeType.Choice)
                {
                    if (node.choices.Count == 0) errors.Add(prefix + "没有选项");
                    foreach (var choice in node.choices)
                    {
                        if (choice == null) { errors.Add(prefix + "空选项"); continue; }
                        if (string.IsNullOrWhiteSpace(choice.text)) errors.Add(prefix + "选项文本为空");
                        if (choice.conditional) CheckCondition(choice.condition, variables, errors, prefix);
                        CheckOperations(choice.operations, variables, errors, prefix);
                    }
                }
                if (node.type == NovelNodeType.Command)
                {
                    if (float.IsNaN(node.seconds) || float.IsInfinity(node.seconds) || node.seconds < 0) errors.Add(prefix + "演出时间无效");
                    if (node.portraitSlot < 0 || node.portraitSlot >= NovelPresentationState.PortraitCapacity) errors.Add(prefix + "立绘槽位越界");
                    if ((node.command == NovelCommandType.PortraitTransform || node.command == NovelCommandType.BackgroundTransform) && (node.pose == null || !Finite(node.pose.position.x) || !Finite(node.pose.position.y) || !Finite(node.pose.scale.x) || !Finite(node.pose.scale.y) || !Finite(node.pose.rotation) || !Finite(node.pose.alpha) || node.pose.alpha < 0 || node.pose.alpha > 1)) errors.Add(prefix + "立绘变换无效");
                    if (node.command == NovelCommandType.Shake && (!Finite(node.shakeStrength) || !Finite(node.shakeFrequency) || node.shakeStrength < 0 || node.shakeFrequency < 0)) errors.Add(prefix + "震动参数无效");
                    bool image = node.command == NovelCommandType.Background || node.command == NovelCommandType.Portrait || node.command == NovelCommandType.ShowCG;
                    if (node.command == NovelCommandType.PortraitExpression && story.FindResource(story.ResolveExpression(node.characterId, node.expressionId))?.image == null) errors.Add(prefix + "角色差分不存在或图片缺失");
                    bool audio = node.command == NovelCommandType.Music || node.command == NovelCommandType.Sound || node.command == NovelCommandType.Ambient;
                    if (node.command == NovelCommandType.AudioVolume && (!Finite(node.volume) || node.volume < 0 || node.volume > 1 || (int)node.audioBus < 0 || (int)node.audioBus > 3)) errors.Add(prefix + "音轨音量参数无效");
                    var resource = story.FindResource(node.resourceId);
                    if (image && (resource == null || resource.image == null)) errors.Add(prefix + "缺少图像资源");
                    if (audio && (resource == null || resource.audio == null)) errors.Add(prefix + "缺少音频资源");
                }
            }
            return errors;
        }

        public static List<string> Unreachable(NovelStoryAsset story)
        {
            var reached = new HashSet<NovelNodeAsset>();
            var pending = new Stack<NovelNodeAsset>();
            if (story.entryNode != null) pending.Push(story.entryNode);
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                if (!reached.Add(node)) continue;
                foreach (var target in node.Targets()) if (target != null) pending.Push(target);
            }
            return story.nodes.Where(n => n != null && !reached.Contains(n)).Select(n => "不可达节点：" + n.nodeId).ToList();
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static void CheckIds(IEnumerable<string> source, string kind, List<string> errors)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in source) if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) errors.Add(kind + " ID 为空或重复：" + id);
        }

        private static void CheckCondition(NovelCondition condition, Dictionary<string, NovelValueType> variables, List<string> errors, string prefix)
        {
            if (condition == null || condition.variable == null || !variables.TryGetValue(condition.variable, out var type) || condition.value == null || type != condition.value.type)
                errors.Add(prefix + "条件变量不存在或类型不匹配");
        }

        private static void CheckOperations(IEnumerable<NovelOperation> operations, Dictionary<string, NovelValueType> variables, List<string> errors, string prefix)
        {
            foreach (var op in operations)
            {
                if (op == null || op.variable == null || !variables.TryGetValue(op.variable, out var type) || op.value == null || op.value.type != type)
                    errors.Add(prefix + "操作变量不存在或类型不匹配");
                else if (op.operation != NovelOperationType.Assign && type != NovelValueType.Integer && type != NovelValueType.Number)
                    errors.Add(prefix + "非数值变量不能加减");
            }
        }
    }
}
