using System;
using System.Collections.Generic;

namespace Lament.SceneGraph
{
    public static class SceneGraphModes
    {
        public const string None = "none";
        public const string Async = "async";
        public const string Jump = "jump";
        public const string Direct = "direct";
        public const string Event = "event";
    }

    public static class SceneGraphSourceKinds
    {
        public const string MenuButton = "UIMenuButton";
        public const string NovelEntry = "NovelEntryPoint";
        public const string ButtonEvent = "ButtonEvent";
        /// <summary>过场播完后由 SceneFlowManager 按 WindowDatabase 跳转表决定去向。</summary>
        public const string SceneFlow = "AnimSceneTransition";
    }

    [Serializable]
    public sealed class SceneGraphSource
    {
        public string objectPath;
        public string kind;
        public string displayName;
        public string windowId;
        public bool isJump;
        public string runtimeScene;
        public string eventTarget;
        public string mode = SceneGraphModes.None;
        public string targetSceneName;
        public string targetScenePath;
        public bool resolved;
    }

    /// <summary>
    /// Runtime-only view data: an edge that lands on a scene, shown inside that scene's card
    /// so a target-only scene (e.g. Settings) is not mistaken for an unconfigured one.
    /// </summary>
    public sealed class SceneIncomingRef
    {
        public string sceneName;
        public string displayName;
        public string kind;
        public string mode = SceneGraphModes.None;
    }

    [Serializable]
    public sealed class SceneGraphWindowDef
    {
        public string objectPath;
        public string id;
        public string sceneName;
        public bool isJump;
        public string targetScenePath;
        public bool resolved;
    }

    [Serializable]
    public sealed class SceneGraphScene
    {
        public string scenePath;
        public string sceneName;
        public int buildIndex = -1;
        public string role = "other";
        public float x;
        public float y;
        public bool collapsed;
        public List<SceneGraphSource> sources = new List<SceneGraphSource>();
        public List<SceneGraphWindowDef> windowDefs = new List<SceneGraphWindowDef>();
        public List<SceneGraphDiagnostic> diagnostics = new List<SceneGraphDiagnostic>();
    }

    [Serializable]
    public sealed class SceneGraphDiagnostic
    {
        public string level = "info";
        public string message;
        public string scenePath;
        public string objectPath;
    }

    [Serializable]
    public sealed class SceneGraphLayoutData
    {
        public int version = 1;
        public List<SceneGraphNodeLayout> nodes = new List<SceneGraphNodeLayout>();
        public List<SceneGraphEdgeColor> edgeColors = new List<SceneGraphEdgeColor>();
        public float miniX = 10f;
        public float miniY = 10f;
    }

    /// <summary>A hand-picked edge color, keyed by source so it survives rescans.</summary>
    [Serializable]
    public sealed class SceneGraphEdgeColor
    {
        public string key;
        public string color;
    }

    /// <summary>Stable identity for one connection, used for the custom color lookup.</summary>
    public static class SceneGraphEdgeKey
    {
        public static string ForSource(string scenePath, SceneGraphSource source)
            => (scenePath ?? string.Empty) + "|" + (source.kind ?? string.Empty) + "|" + (source.objectPath ?? string.Empty);

        public static string ForWindow(string scenePath, string id)
            => (scenePath ?? string.Empty) + "|window|" + (id ?? string.Empty);
    }

    [Serializable]
    public sealed class SceneGraphNodeLayout
    {
        public string scenePath;
        public float x;
        public float y;
        public bool collapsed;
    }
}
