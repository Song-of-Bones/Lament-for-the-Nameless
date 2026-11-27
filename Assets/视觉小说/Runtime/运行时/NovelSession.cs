using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lament.VisualNovel
{
    public enum NovelPlaybackStatus { Stopped, Dialogue, Choice, Wait, Ended, Faulted, TextInput, LuaEffect }

    // Pure state machine: UI, save I/O and Unity scene lifetime stay outside execution.
    public sealed class NovelSession
    {
        public NovelStoryAsset Story { get; private set; }
        public NovelNodeAsset Current { get; private set; }
        public NovelVariableStore Variables { get; private set; }
        public NovelPlaybackStatus Status { get; private set; }
        public NovelSaveData State { get; private set; }
        public IReadOnlyList<NovelChoice> Choices => choices;
        public bool CurrentWasRead { get; private set; }
        public string Error { get; private set; }
        public int Visit { get; private set; }
        public event Action Changed;
        public event Action<NovelNodeAsset> Command;
        private readonly List<NovelChoice> choices = new List<NovelChoice>();

        public void Start(NovelStoryAsset story, string entryNodeId = null)
        {
            ValidateStory(story);
            var entry = string.IsNullOrEmpty(entryNodeId) ? story.entryNode : story.FindNode(entryNodeId);
            if (entry == null) throw new InvalidOperationException("预览入口节点不存在：" + entryNodeId);
            Story = story;
            Variables = new NovelVariableStore(story.variables);
            State = new NovelSaveData { storyId = story.storyId, storyName = story.displayName, storyVersion = story.schemaVersion };
            State.presentation.EnsureDefaults();
            Error = null;
            Enter(entry);
        }

        public void Restore(NovelStoryAsset story, NovelSaveData saved)
        {
            ValidateStory(story);
            if (saved == null || saved.version != 1 || saved.storyId != story.storyId || saved.storyVersion != story.schemaVersion)
                throw new InvalidOperationException("存档与剧情版本不匹配");
            var node = story.FindNode(saved.nodeId);
            if (node == null || (node.type != NovelNodeType.Dialogue && node.type != NovelNodeType.Choice && node.type != NovelNodeType.TextInput && node.type != NovelNodeType.LuaEffect && node.type != NovelNodeType.Wait && !(node.type == NovelNodeType.Command && node.waitForCompletion)))
                throw new InvalidOperationException("存档不是有效的剧情检查点");
            if (saved.variables == null || saved.variables.Count != story.variables.Count || saved.presentation == null || saved.presentation.portraits == null || (saved.presentation.portraits.Length != 3 && saved.presentation.portraits.Length != NovelPresentationState.PortraitCapacity) || saved.history == null || saved.readNodes == null)
                throw new InvalidOperationException("存档数据不完整");
            var restored = new NovelVariableStore(story.variables);
            var ids = new HashSet<string>();
            foreach (var item in saved.variables)
            {
                if (item == null || !ids.Add(item.id)) throw new InvalidOperationException("存档变量重复或无效");
            }
            restored.Restore(saved.variables);
            var restoredChoices = new List<NovelChoice>();
            if (node.type == NovelNodeType.Choice)
            {
                foreach (var option in node.choices)
                    if (!option.conditional || restored.Evaluate(option.condition)) restoredChoices.Add(option);
                if (restoredChoices.Count == 0) throw new InvalidOperationException("存档没有有效选项");
            }
            if (float.IsNaN(saved.waitRemaining) || float.IsInfinity(saved.waitRemaining) || saved.waitRemaining < 0)
                throw new InvalidOperationException("存档等待时间无效");
            Story = story;
            Variables = restored;
            State = saved.Copy();
            State.presentation.EnsureDefaults();
            if (State.sampledNodes == null) State.sampledNodes = new List<string>();
            Current = node;
            Error = null;
            CurrentWasRead = State.readNodes.Contains(node.nodeId);
            choices.Clear();
            choices.AddRange(restoredChoices);
            Visit++;
            Status = node.type == NovelNodeType.LuaEffect ? NovelPlaybackStatus.LuaEffect :
                node.type == NovelNodeType.TextInput ? NovelPlaybackStatus.TextInput :
                node.type == NovelNodeType.Choice ? NovelPlaybackStatus.Choice :
                node.type == NovelNodeType.Dialogue ? NovelPlaybackStatus.Dialogue : NovelPlaybackStatus.Wait;
            Changed?.Invoke();
        }

        private static void ValidateStory(NovelStoryAsset story)
        {
            var errors = NovelStoryValidator.Validate(story);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
        }

        public void CompleteText()
        {
            if (Status != NovelPlaybackStatus.Dialogue || State.textComplete) return;
            State.textComplete = true;
            if (!State.readNodes.Contains(Current.nodeId)) State.readNodes.Add(Current.nodeId);
            Changed?.Invoke();
        }

        public void Continue()
        {
            if (Status == NovelPlaybackStatus.Dialogue)
            {
                if (!State.textComplete) { CompleteText(); return; }
                Enter(Current.next);
            }
            else if (Status == NovelPlaybackStatus.Wait && Current.waitForInput) Enter(Current.next);
        }

        public void Tick(float seconds)
        {
            if (Status != NovelPlaybackStatus.Wait || Current.waitForInput) return;
            State.waitRemaining = Mathf.Max(0, State.waitRemaining - Mathf.Max(0, seconds));
            if (State.waitRemaining <= 0) Enter(Current.next);
        }

        public void Select(int index)
        {
            if (Status != NovelPlaybackStatus.Choice || index < 0 || index >= choices.Count) return;
            var selected = choices[index];
            try
            {
                foreach (var operation in selected.operations) Variables.Apply(operation);
                Enter(selected.target);
            }
            catch (Exception ex) { Fail(ex.Message); }
        }

        public void CompleteLuaEffect()
        {
            if (Status == NovelPlaybackStatus.LuaEffect) Enter(Current.next);
        }

        public void FailLuaEffect(string message)
        {
            if (Status == NovelPlaybackStatus.LuaEffect) Fail(message);
        }

        public bool SubmitText(string input)
        {
            if (Status != NovelPlaybackStatus.TextInput) return false;
            input = input?.Trim();
            if (string.IsNullOrEmpty(input) || new System.Globalization.StringInfo(input).LengthInTextElements > Current.inputMaxLength || input.IndexOfAny(new[] { '<', '>', '{', '}', '\r', '\n', '\t' }) >= 0) return false;
            Variables.Apply(new NovelOperation { variable = Current.inputVariable, value = new NovelValue { type = NovelValueType.String, text = input } });
            Enter(Current.next);
            return true;
        }

        public string FormatText(string template)
        {
            if (string.IsNullOrEmpty(template) || Variables == null) return template ?? "";
            foreach (var variable in Variables.Export())
            {
                string value;
                switch (variable.initial.type)
                {
                    case NovelValueType.String: value = variable.initial.text ?? ""; break;
                    case NovelValueType.Integer: value = variable.initial.integer.ToString(System.Globalization.CultureInfo.InvariantCulture); break;
                    case NovelValueType.Number: value = variable.initial.number.ToString(System.Globalization.CultureInfo.InvariantCulture); break;
                    default: value = variable.initial.boolean ? "true" : "false"; break;
                }
                value = value.Replace("<", "＜").Replace(">", "＞");
                template = template.Replace("{" + variable.id + "}", value);
            }
            return template;
        }

        public NovelSaveData Snapshot()
        {
            if (Status != NovelPlaybackStatus.Dialogue && Status != NovelPlaybackStatus.Choice && Status != NovelPlaybackStatus.TextInput && Status != NovelPlaybackStatus.LuaEffect && Status != NovelPlaybackStatus.Wait)
                throw new InvalidOperationException("当前不在可存档的检查点");
            var snapshot = State.Copy();
            snapshot.variables = Variables.Export();
            return snapshot;
        }

        private void Enter(NovelNodeAsset node)
        {
            try
            {
                // Bound synchronous work so malformed jump cycles cannot freeze the main thread.
                for (int budget = 0; budget < 1024; budget++)
                {
                    if (node == null) throw new InvalidOperationException("剧情目标为空");
                    Current = node;
                    Visit++;
                    State.nodeId = node.nodeId;
                    choices.Clear();
                    switch (node.type)
                    {
                        case NovelNodeType.Dialogue:
                            Status = NovelPlaybackStatus.Dialogue;
                            CurrentWasRead = State.readNodes.Contains(node.nodeId);
                            State.textComplete = false;
                            var character = Story.FindCharacter(node.characterId);
                            State.history.Add(new NovelHistoryEntry { nodeId = node.nodeId, speaker = character?.displayName ?? "", text = FormatText(node.text) });
                            if (State.history.Count > 500) State.history.RemoveAt(0);
                            Changed?.Invoke();
                            return;
                        case NovelNodeType.LuaEffect:
                            Status = NovelPlaybackStatus.LuaEffect;
                            Changed?.Invoke();
                            return;
                        case NovelNodeType.TextInput:
                            Status = NovelPlaybackStatus.TextInput;
                            Changed?.Invoke();
                            return;
                        case NovelNodeType.Choice:
                            FillChoices();
                            Changed?.Invoke();
                            return;
                        case NovelNodeType.Wait:
                            Status = NovelPlaybackStatus.Wait;
                            State.waitRemaining = node.seconds;
                            Changed?.Invoke();
                            return;
                        case NovelNodeType.Conditional:
                            node = Variables.Evaluate(node.condition) ? node.whenTrue : node.whenFalse;
                            break;
                        case NovelNodeType.SampleFlags:
                            SampleFlags(node);
                            node = node.next;
                            break;
                        case NovelNodeType.SetVariable:
                            foreach (var operation in node.operations) Variables.Apply(operation);
                            node = node.next;
                            break;
                        case NovelNodeType.Command:
                            ApplyCommand(node);
                            Command?.Invoke(node);
                            if (node.waitForCompletion && node.seconds > 0)
                            {
                                Status = NovelPlaybackStatus.Wait;
                                State.waitRemaining = node.seconds;
                                Changed?.Invoke();
                                return;
                            }
                            node = node.next;
                            break;
                        case NovelNodeType.End:
                            Status = NovelPlaybackStatus.Ended;
                            Changed?.Invoke();
                            return;
                        default: throw new InvalidOperationException("未知节点类型");
                    }
                }
                throw new InvalidOperationException("连续执行超过 1024 个节点，可能存在无等待循环");
            }
            catch (Exception ex) { Fail(ex.Message); }
        }

        private void FillChoices()
        {
            Status = NovelPlaybackStatus.Choice;
            foreach (var choice in Current.choices)
                if (!choice.conditional || Variables.Evaluate(choice.condition)) choices.Add(choice);
            if (choices.Count == 0) throw new InvalidOperationException("没有满足条件的可选项：" + Current.nodeId);
        }

        private void SampleFlags(NovelNodeAsset node)
        {
            if (State.sampledNodes.Contains(node.nodeId)) return;
            var indices = new List<int>();
            for (int i = 0; i < node.sampleVariables.Count; i++) indices.Add(i);
            var random = new System.Random();
            for (int i = indices.Count - 1; i > 0; i--)
            {
                int other = random.Next(i + 1);
                int value = indices[i]; indices[i] = indices[other]; indices[other] = value;
            }
            for (int i = 0; i < indices.Count; i++)
                Variables.Apply(new NovelOperation { variable = node.sampleVariables[indices[i]], value = new NovelValue { type = NovelValueType.Boolean, boolean = i < node.sampleCount } });
            State.sampledNodes.Add(node.nodeId);
        }

        private void ApplyCommand(NovelNodeAsset node)
        {
            State.presentation.EnsureDefaults();
            switch (node.command)
            {
                case NovelCommandType.ShowCG: State.presentation.cg = node.resourceId; break;
                case NovelCommandType.HideCG: State.presentation.cg = ""; break;
                case NovelCommandType.Ambient: State.presentation.ambient = node.resourceId; break;
                case NovelCommandType.StopAmbient: State.presentation.ambient = ""; break;
                case NovelCommandType.AudioVolume: State.presentation.volumes[(int)node.audioBus] = node.volume; break;
                case NovelCommandType.BackgroundTransform:
                    State.presentation.backgroundPose = new NovelPortraitPose { position = node.pose.position, scale = node.pose.scale, rotation = node.pose.rotation, alpha = node.pose.alpha };
                    break;
                case NovelCommandType.Background: State.presentation.background = node.resourceId; break;
                case NovelCommandType.Portrait: State.presentation.portraits[node.portraitSlot] = node.resourceId; break;
                case NovelCommandType.PortraitExpression: State.presentation.portraits[node.portraitSlot] = Story.ResolveExpression(node.characterId, node.expressionId); break;
                case NovelCommandType.HidePortrait: State.presentation.portraits[node.portraitSlot] = ""; break;
                case NovelCommandType.Music: State.presentation.music = node.resourceId; break;
                case NovelCommandType.StopMusic: State.presentation.music = ""; break;
                case NovelCommandType.Fade: State.presentation.overlay = node.color; break;
                case NovelCommandType.PortraitTransform:
                    State.presentation.EnsureDefaults();
                    State.presentation.poses[node.portraitSlot] = new NovelPortraitPose
                    {
                        position = node.pose.position, scale = node.pose.scale,
                        rotation = node.pose.rotation, alpha = node.pose.alpha
                    };
                    break;
            }
        }

        private void Fail(string message)
        {
            Error = "[" + Story.storyId + "/" + Current?.nodeId + "] " + message;
            Status = NovelPlaybackStatus.Faulted;
            Changed?.Invoke();
        }
    }
}
