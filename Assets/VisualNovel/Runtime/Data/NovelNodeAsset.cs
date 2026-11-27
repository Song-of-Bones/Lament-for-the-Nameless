using System.Collections.Generic;
using UnityEngine;

namespace Lament.VisualNovel
{
    public sealed class NovelNodeAsset : ScriptableObject
    {
        public string nodeId;
        public NovelNodeType type;
        public NovelNodeAsset next;
        public string characterId;
        [TextArea(3, 12)] public string text;
        public AudioClip voice;
        public List<NovelChoice> choices = new List<NovelChoice>();
        public NovelCondition condition = new NovelCondition();
        public NovelNodeAsset whenTrue;
        public NovelNodeAsset whenFalse;
        public List<NovelOperation> operations = new List<NovelOperation>();
        [Min(0)] public float seconds;
        public bool waitForInput;
        public NovelCommandType command;
        public string resourceId;
        [Tooltip("PortraitExpression 命令使用 characterId + expressionId 查找差分")]
        public string expressionId;
        [Range(0, 5)] public int portraitSlot = 1;
        public Color color = Color.black;
        [Tooltip("命令演出期间阻止推进；不勾选则与后续节点并行")]
        public bool waitForCompletion;
        public NovelEase easing = NovelEase.Smooth;
        [Tooltip("相对该立绘槽初始位置的偏移，单位为 Canvas 参考像素")]
        public NovelPortraitPose pose = new NovelPortraitPose();
        [Min(0)] public float shakeStrength = 18;
        [Min(0)] public float shakeFrequency = 24;
        [Header("序章：输入与音轨")]
        public string inputVariable;
        [Range(1, 64)] public int inputMaxLength = 16;
        [Range(0, 1)] public float volume = 1;
        public NovelAudioBus audioBus;
        [Header("首次抽样标记")]
        public List<string> sampleVariables = new List<string>();
        [Min(0)] public int sampleCount = 2;
        [Header("Lua 自定义临时演出")]
        [Tooltip("拖入 .lua.txt TextAsset；执行期间暂停剧情，结束后清理临时图像")]
        public TextAsset luaScript;
        [Range(0.1f, 120f)] public float luaTimeout = 30f;
        public bool returnToLobby = true;

        public IEnumerable<NovelNodeAsset> Targets()
        {
            if (type == NovelNodeType.End) yield break;
            if (type == NovelNodeType.Choice)
            {
                foreach (var choice in choices) yield return choice?.target;
            }
            else if (type == NovelNodeType.Conditional)
            {
                yield return whenTrue;
                yield return whenFalse;
            }
            else yield return next;
        }
    }
}
