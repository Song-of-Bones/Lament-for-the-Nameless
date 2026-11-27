using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lament.VisualNovel
{
    [CreateAssetMenu(menuName = "视觉小说/剧情", fileName = "新剧情")]
    public sealed class NovelStoryAsset : ScriptableObject
    {
        public string storyId;
        public string displayName;
        public int schemaVersion = 1;
        public NovelNodeAsset entryNode;
        public List<NovelNodeAsset> nodes = new List<NovelNodeAsset>();
        public List<NovelVariableDefinition> variables = new List<NovelVariableDefinition>();
        public List<NovelCharacterDefinition> characters = new List<NovelCharacterDefinition>();
        public List<NovelResourceDefinition> resources = new List<NovelResourceDefinition>();

        public NovelNodeAsset FindNode(string id) => nodes.Find(n => n != null && n.nodeId == id);
        public NovelResourceDefinition FindResource(string id) => resources.Find(r => r != null && r.id == id);
        public NovelCharacterDefinition FindCharacter(string id) => characters.Find(c => c != null && c.id == id);
        public string ResolveExpression(string characterId, string expressionId)
        {
            var character = FindCharacter(characterId);
            return character?.expressions?.Find(e => e != null && e.id == expressionId)?.resourceId;
        }
    }

    public enum NovelNodeType { Dialogue, Choice, Conditional, SetVariable, Wait, Command, End, TextInput, SampleFlags, LuaEffect }
    public enum NovelValueType { Boolean, Integer, Number, String }
    public enum NovelComparison { Equal, NotEqual, Greater, Less, GreaterOrEqual, LessOrEqual }
    public enum NovelOperationType { Assign, Add, Subtract }
    public enum NovelCommandType { Background, Portrait, HidePortrait, Music, StopMusic, Sound, Fade, PortraitTransform, Flash, Shake, PortraitExpression, ShowCG, HideCG, BackgroundTransform, Ambient, StopAmbient, AudioVolume, Distortion, Shatter }
    public enum NovelAudioBus { Music, Ambient, Voice, Sound }
    public enum NovelEase { Linear, Smooth, EaseIn, EaseOut }

    [Serializable]
    public sealed class NovelPortraitPose
    {
        public Vector2 position = Vector2.zero;
        public Vector2 scale = Vector2.one;
        public float rotation;
        [Range(0, 1)] public float alpha = 1;
    }

    [Serializable]
    public sealed class NovelValue
    {
        public NovelValueType type;
        public bool boolean;
        public int integer;
        public float number;
        public string text = "";
        public NovelValue Copy() => new NovelValue { type = type, boolean = boolean, integer = integer, number = number, text = text };
    }

    [Serializable]
    public sealed class NovelVariableDefinition
    {
        public string id;
        public NovelValue initial = new NovelValue();
    }

    [Serializable]
    public sealed class NovelCharacterDefinition
    {
        public string id;
        public string displayName;
        public Color nameColor = Color.white;
        public Sprite portrait;
        public List<NovelExpressionDefinition> expressions = new List<NovelExpressionDefinition>();
    }

    [Serializable]
    public sealed class NovelExpressionDefinition
    {
        public string id;
        public string resourceId;
    }

    [Serializable]
    public sealed class NovelResourceDefinition
    {
        public string id;
        public Sprite image;
        public AudioClip audio;
    }

    [Serializable]
    public sealed class NovelCondition
    {
        public string variable;
        public NovelComparison comparison;
        public NovelValue value = new NovelValue();
    }

    [Serializable]
    public sealed class NovelOperation
    {
        public string variable;
        public NovelOperationType operation;
        public NovelValue value = new NovelValue();
    }

    [Serializable]
    public sealed class NovelChoice
    {
        public string text;
        public bool conditional;
        public NovelCondition condition = new NovelCondition();
        public List<NovelOperation> operations = new List<NovelOperation>();
        public NovelNodeAsset target;
    }
}
