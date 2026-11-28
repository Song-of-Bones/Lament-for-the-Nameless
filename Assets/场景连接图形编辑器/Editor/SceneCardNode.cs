using System;
using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lament.SceneGraph
{
    /// <summary>
    /// One card per scene: title, button/entry rows, WindowDatabase entries and the
    /// connections other scenes point at this one.
    /// Each row has an output port; the card has a single input port in the left gutter.
    /// </summary>
    public sealed class SceneCardNode : Node
    {
        // Reserved column on the left so the input port never sits on top of the text.
        private const float Gutter = 22f;
        private const int MaxIncomingRows = 8;

        public SceneGraphScene Scene { get; }
        public IReadOnlyList<SceneIncomingRef> Incoming { get; }
        public Port InputPort { get; }
        public readonly List<Port> SourcePorts = new List<Port>();
        public readonly List<Port> WindowDefPorts = new List<Port>();

        /// <summary>折叠/展开后通知窗口保存布局。</summary>
        public Action CollapsedChanged;

        private readonly VisualElement _body = new VisualElement();

        public SceneCardNode(SceneGraphScene scene, IReadOnlyList<SceneIncomingRef> incoming)
        {
            Scene = scene;
            Incoming = incoming ?? new List<SceneIncomingRef>();
            title = scene.sceneName + (scene.buildIndex >= 0 ? "  #" + scene.buildIndex : "  (not in build)");
            name = scene.scenePath;
            style.width = 340;

            _body.style.paddingLeft = Gutter;
            _body.style.paddingRight = 6;
            _body.style.paddingBottom = 6;
            contentContainer.Add(_body);

            // inputContainer can keep a NaN layout in GraphView; put the single input port
            // in the body so its world position is always valid.
            InputPort = Port.Create<Edge>(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(bool));
            InputPort.portName = string.Empty;
            InputPort.portColor = SceneGraphPalette.Incoming;
            InputPort.tooltip = "连入点：把其它场景按钮/入口右侧的圆点拖到这里";
            InputPort.style.position = Position.Absolute;
            InputPort.style.left = 0f;
            InputPort.style.top = 2f;
            _body.Add(InputPort);

            BuildBody();
        }

        private void BuildBody()
        {
            bool hasRows = Scene.sources.Count > 0 || Scene.windowDefs.Count > 0;

            if (Scene.sources.Count > 0)
            {
                _body.Add(SectionLabel("按钮 / 入口"));
                foreach (var source in Scene.sources)
                    AddSourceRow(source);
            }

            if (Scene.windowDefs.Count > 0)
            {
                _body.Add(SectionLabel("窗口定义"));
                foreach (var def in Scene.windowDefs)
                    AddWindowDefRow(def);
            }

            if (Incoming.Count > 0)
            {
                _body.Add(SectionLabel("被其它场景连接到此"));
                int shown = 0;
                foreach (var incoming in Incoming)
                {
                    if (shown++ >= MaxIncomingRows)
                    {
                        _body.Add(Hint($"…另外 {Incoming.Count - MaxIncomingRows} 条"));
                        break;
                    }

                    AddIncomingRow(incoming);
                }
            }

            if (!hasRows && Incoming.Count == 0)
                _body.Add(Hint("（本场景没有按钮或窗口配置，也没有其它场景连入）"));
            else if (!hasRows)
                _body.Add(Hint("（仅作为连接目标，内部没有按钮或窗口配置）"));

            if (Scene.diagnostics.Count > 0)
            {
                _body.Add(SectionLabel("诊断"));
                int shown = 0;
                foreach (var diagnostic in Scene.diagnostics)
                {
                    if (shown++ >= 3) break;
                    _body.Add(Hint("• " + diagnostic.message));
                }
            }
        }

        private void AddSourceRow(SceneGraphSource source)
        {
            Color color = SceneGraphPalette.ForMode(source.mode);

            var row = Row();
            var dot = SceneGraphPalette.Swatch(color, 8f);
            dot.style.marginRight = 6;
            row.Add(dot);

            var label = new Label(FormatSource(source));
            label.style.flexGrow = 1;
            label.tooltip = source.objectPath + "\n" + source.kind;
            row.Add(label);

            var port = Port.Create<Edge>(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
            port.portName = string.Empty;
            port.portColor = color;
            port.tooltip = "拖出连线到目标场景卡片";
            port.userData = source;
            row.Add(port);

            SourcePorts.Add(port);
            _body.Add(row);
        }

        private void AddWindowDefRow(SceneGraphWindowDef def)
        {
            Color color = SceneGraphPalette.ForWindowDef(def.isJump);

            var row = Row();
            var dot = SceneGraphPalette.Swatch(color, 8f);
            dot.style.marginRight = 6;
            row.Add(dot);

            var label = new Label($"{def.id} [{(def.isJump ? "跳转" : "异步")}] → {def.sceneName}");
            label.style.flexGrow = 1;
            label.tooltip = def.objectPath;
            row.Add(label);

            var port = Port.Create<Edge>(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
            port.portName = string.Empty;
            port.portColor = color;
            port.tooltip = "拖出连线到目标场景卡片";
            port.userData = def;
            row.Add(port);

            WindowDefPorts.Add(port);
            _body.Add(row);
        }

        private void AddIncomingRow(SceneIncomingRef incoming)
        {
            Color color = SceneGraphPalette.ForMode(incoming.mode);

            var row = Row();
            var dot = SceneGraphPalette.Swatch(color, 7f);
            dot.style.marginLeft = 2;
            dot.style.marginRight = 7;
            row.Add(dot);

            var label = new Label(
                $"{incoming.sceneName} · {incoming.displayName} [{SceneGraphPalette.LabelForMode(incoming.mode)}]");
            label.style.flexGrow = 1;
            label.style.fontSize = 10;
            label.style.color = new Color(0.72f, 0.75f, 0.79f);
            label.tooltip = incoming.kind + " → " + Scene.sceneName;
            row.Add(label);

            _body.Add(row);
        }

        private static VisualElement Row()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.minHeight = 20;
            row.style.marginBottom = 1;
            return row;
        }

        private static string FormatSource(SceneGraphSource source)
        {
            string mode = SceneGraphPalette.LabelForMode(source.mode);

            if (source.mode == SceneGraphModes.Event)
                return $"{source.displayName} [{mode}] {source.eventTarget}";

            string target = !string.IsNullOrEmpty(source.targetSceneName)
                ? source.targetSceneName
                : string.IsNullOrEmpty(source.windowId) ? "—" : "ID:" + source.windowId;

            return $"{source.displayName} [{mode}] → {target}";
        }

        private static Label SectionLabel(string text)
        {
            var label = new Label(text);
            label.style.fontSize = 10;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginTop = 4;
            label.style.marginBottom = 2;
            label.style.color = new Color(0.6f, 0.65f, 0.7f);
            return label;
        }

        private static Label Hint(string text)
        {
            var label = new Label(text);
            label.style.fontSize = 10;
            label.style.color = new Color(0.55f, 0.55f, 0.55f);
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        public float PreferredHeight
        {
            get
            {
                int rows = Scene.sources.Count + Scene.windowDefs.Count + Mathf.Min(Incoming.Count, MaxIncomingRows + 1);
                int sections = 0;
                if (Scene.sources.Count > 0) sections++;
                if (Scene.windowDefs.Count > 0) sections++;
                if (Incoming.Count > 0) sections++;
                if (Scene.diagnostics.Count > 0) sections++;

                float height = 40f + rows * 22f + sections * 20f + 24f;
                if (rows == 0) height += 22f;
                return Mathf.Max(120f, height);
            }
        }

        public void SetCollapsed(bool collapsed)
        {
            Scene.collapsed = collapsed;
            expanded = !collapsed;             // 与 Node 自带状态保持一致，否则折叠按钮会与我们打架
            _body.style.display = collapsed ? DisplayStyle.None : DisplayStyle.Flex;
            RefreshExpandedState();
            RefreshPorts();
            CollapsedChanged?.Invoke();
        }

        /// <summary>接管卡片左上角的折叠按钮：同步 expanded 与布局保存。</summary>
        protected override void ToggleCollapse()
        {
            base.ToggleCollapse();
            Scene.collapsed = !expanded;
            _body.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            RefreshPorts();
            CollapsedChanged?.Invoke();
        }
    }
}
