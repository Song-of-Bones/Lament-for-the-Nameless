using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Lament.SceneGraph
{
    /// <summary>
    /// Minimal scene connection graph: one card per Build scene, one row per button/entry,
    /// edges to the target scene, drag-to-writeback and a small movable mini map.
    /// </summary>
    public sealed class SceneGraphWindow : EditorWindow
    {
        private const float MiniMapWidth = 220f;
        private const float MiniMapHeight = 150f;

        private SceneGraphView _graph;
        private VisualElement _graphHost;
        private Label _status;
        private List<SceneGraphScene> _scenes = new List<SceneGraphScene>();
        private readonly Dictionary<string, SceneCardNode> _nodes =
            new Dictionary<string, SceneCardNode>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Port, SceneCardNode> _portOwners =
            new Dictionary<Port, SceneCardNode>();
        private readonly Dictionary<Port, SceneGraphSource> _sourcePorts =
            new Dictionary<Port, SceneGraphSource>();
        private readonly Dictionary<Port, SceneGraphWindowDef> _defPorts =
            new Dictionary<Port, SceneGraphWindowDef>();
        private readonly Dictionary<Edge, string> _edgeKeys = new Dictionary<Edge, string>();
        private readonly Dictionary<string, List<Edge>> _edgesByKey =
            new Dictionary<string, List<Edge>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Color> _customColors =
            new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Edge> _edges = new List<Edge>();
        private SceneGraphLayoutData _layout;
        private MiniMap _miniMap;
        private bool _scanQueued;
        private bool _saveQueued;
        private double _routeAt;
        private bool _routeTickHooked;
        private int _routeAttempts;
        private Vector2 _pointerPanelPosition;

        // 项目自己的编辑器工具都挂在中文顶级菜单「工具」下（工具/视觉小说、工具/项目），
        // 英文 Tools 会被本地化成同名的另一个「工具」菜单，放到那里用户找不到。
        [MenuItem("工具/项目/场景连接图 %#g", false, 1)]
        public static void Open()
        {
            var window = GetWindow<SceneGraphWindow>("场景连接图");
            window.minSize = new Vector2(960, 620);
            window.Show();
            window.Focus();
        }

        private void CreateGUI()
        {
            rootVisualElement.style.flexDirection = FlexDirection.Column;
            rootVisualElement.Add(BuildToolbar());

            _graphHost = new VisualElement { style = { flexGrow = 1 } };
            rootVisualElement.Add(_graphHost);

            _layout = SceneGraphLayoutStore.Load();
            LoadCustomColors();
            Undo.undoRedoPerformed += OnUndoRedo;
            rootVisualElement.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            Scan();
        }

        private VisualElement BuildToolbar()
        {
            var container = new VisualElement
            {
                style =
                {
                    borderBottomWidth = 1,
                    borderBottomColor = new Color(0f, 0f, 0f, 0.35f)
                }
            };

            var bar = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    height = 28,
                    paddingLeft = 4,
                    paddingRight = 4
                }
            };

            bar.Add(ToolButton("扫描 / 刷新", Scan));
            bar.Add(ToolButton("自动布局", AutoLayout));
            bar.Add(ToolButton("保存布局", SaveLayout));
            bar.Add(ToolButton("全部展开", () => SetAllCollapsed(false)));
            bar.Add(ToolButton("全部折叠", () => SetAllCollapsed(true)));
            bar.Add(ToolButton("使用说明", ShowHelp));

            _status = new Label("就绪");
            _status.style.marginLeft = 12;
            _status.style.fontSize = 11;
            _status.style.color = new Color(0.7f, 0.72f, 0.75f);
            bar.Add(_status);

            container.Add(bar);
            container.Add(BuildLegend());
            return container;
        }

        private static VisualElement BuildLegend()
        {
            var legend = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    height = 20,
                    paddingLeft = 8
                }
            };

            legend.Add(SceneGraphPalette.LegendItem(SceneGraphPalette.Async, "异步窗口"));
            legend.Add(SceneGraphPalette.LegendItem(SceneGraphPalette.Jump, "跳转场景"));
            legend.Add(SceneGraphPalette.LegendItem(SceneGraphPalette.Direct, "直接进入"));
            legend.Add(SceneGraphPalette.LegendItem(SceneGraphPalette.Event, "事件"));
            legend.Add(SceneGraphPalette.LegendItem(SceneGraphPalette.Unresolved, "未连接"));

            var hint = new Label("连线只走水平 / 垂直 / 45°");
            hint.style.fontSize = 10;
            hint.style.marginLeft = 8;
            hint.style.color = new Color(0.5f, 0.53f, 0.57f);
            legend.Add(hint);

            return legend;
        }

        private static Button ToolButton(string text, Action action)
        {
            var button = new Button(action) { text = text };
            button.style.marginRight = 4;
            return button;
        }

        private void ShowHelp()
        {
            EditorUtility.DisplayDialog(
                "场景连接图",
                "· 拖右侧圆点 → 目标场景卡片：创建/改写连接\n" +
                "· 右键空白：刷新 / 自动布局 / 保存布局\n" +
                "· 右键连线（鼠标停在线上）：自定义颜色（色轮 + #RRGGBB）\n" +
                "· 中键 / Alt+左键移动迷你地图（固定大小）\n" +
                "· 双击场景卡片：打开场景",
                "好");
        }

        private void RebuildGraphView()
        {
            _nodes.Clear();
            _portOwners.Clear();
            _sourcePorts.Clear();
            _defPorts.Clear();
            _edgeKeys.Clear();
            _edgesByKey.Clear();
            _edges.Clear();
            _routeAttempts = 0;
            SceneGraphEdgeRouter.Reset();

            // 只创建一次 GraphView：重建实例会把缩放/平移重置回画布中心，还会重复注册回调。
            if (_graph == null)
            {
                _graph = new SceneGraphView { name = "scene-connection-graph" };
                _graph.style.flexGrow = 1;
                _graph.AddManipulator(new ContentDragger());
                _graph.AddManipulator(new SelectionDragger());
                _graph.AddManipulator(new RectangleSelector());
                _graph.AddManipulator(new ContentZoomer());
                _graph.SetupZoom(0.1f, 2f);
                _graph.graphViewChanged += OnGraphChanged;
                _graph.RegisterCallback<GeometryChangedEvent>(_ => QueueRoute(0.1f));
                // Unity sends ContextualMenuPopulateEvent to the manipulator's own element (the graph),
                // so the right-click target is resolved from the last pointer position instead.
                _graph.RegisterCallback<PointerDownEvent>(evt => _pointerPanelPosition = evt.position,
                    TrickleDown.TrickleDown);
                _graph.RegisterCallback<PointerUpEvent>(evt => _pointerPanelPosition = evt.position,
                    TrickleDown.TrickleDown);
                _graph.RegisterCallback<PointerMoveEvent>(evt => _pointerPanelPosition = evt.position,
                    TrickleDown.TrickleDown);

                // 缩放/平移只改变视图变换，连线网格不会自动重绘，看起来就像"线停在原地"。
                _graph.RegisterCallback<WheelEvent>(_ => SceneGraphEdgeRouter.RepaintAll(_edges),
                    TrickleDown.TrickleDown);
                _graph.RegisterCallback<PointerMoveEvent>(evt =>
                {
                    if (evt.pressedButtons != 0) SceneGraphEdgeRouter.RepaintAll(_edges);
                }, TrickleDown.TrickleDown);

                _graph.AddManipulator(new ContextualMenuManipulator(BuildContextMenu));
                _graphHost.Add(_graph);

                _miniMap = new MiniMap { anchored = false };
                _miniMap.SetPosition(new Rect(_layout.miniX, _layout.miniY, MiniMapWidth, MiniMapHeight));
                SetupMiniMap(_miniMap);
                _graph.Add(_miniMap);
            }
            else
            {
                // 清掉卡片/连线，保留 GraphView 实例与当前的缩放、平移。
                foreach (var staleEdge in _graph.Query<Edge>().ToList()) _graph.RemoveElement(staleEdge);
                foreach (var staleNode in _graph.Query<Node>().ToList()) _graph.RemoveElement(staleNode);
            }
        }

        private void Scan()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                ShowNotification(new GUIContent("播放模式下不能扫描场景"));
                return;
            }

            var previous = SceneManager.GetActiveScene();
            _scenes = SceneGraphScanner.Scan();
            SceneGraphLayoutStore.Apply(_scenes, _layout);
            if (previous.IsValid() && previous.isLoaded)
                SceneManager.SetActiveScene(previous);

            if (_scenes.All(scene => scene.x == 0f && scene.y == 0f))
                ComputeAutoLayoutPositions();

            RebuildGraphView();
            BuildNodes();
        }

        private void BuildNodes()
        {
            if (_graph == null) return;

            var incoming = CollectIncoming();
            var listener = new SceneGraphEdgeListener(this);
            foreach (var scene in _scenes)
            {
                incoming.TryGetValue(scene.scenePath, out var incomingRefs);
                var node = new SceneCardNode(scene, incomingRefs);
                node.CollapsedChanged = QueueSaveLayout;
                node.SetPosition(new Rect(
                    scene.x > 0f ? scene.x : 40f,
                    scene.y > 0f ? scene.y : 40f,
                    340f,
                    node.PreferredHeight));

                node.RegisterCallback<ContextualMenuPopulateEvent>(evt => OnNodeMenu(node, evt));
                node.RegisterCallback<MouseDownEvent>(evt =>
                {
                    if (evt.clickCount == 2) OpenScene(scene.scenePath, false);
                });

                _graph.AddElement(node);
                _nodes[scene.scenePath] = node;
                node.SetCollapsed(scene.collapsed);
                _portOwners[node.InputPort] = node;

                for (int i = 0; i < node.SourcePorts.Count && i < scene.sources.Count; i++)
                {
                    var port = node.SourcePorts[i];
                    var source = scene.sources[i];
                    _sourcePorts[port] = source;
                    _portOwners[port] = node;
                    port.AddManipulator(new EdgeConnector<Edge>(listener));
                    port.RegisterCallback<ContextualMenuPopulateEvent>(evt => OnSourcePortMenu(node, source, evt));
                }

                for (int i = 0; i < node.WindowDefPorts.Count && i < scene.windowDefs.Count; i++)
                {
                    var port = node.WindowDefPorts[i];
                    var def = scene.windowDefs[i];
                    _defPorts[port] = def;
                    _portOwners[port] = node;
                    port.AddManipulator(new EdgeConnector<Edge>(listener));
                    port.RegisterCallback<ContextualMenuPopulateEvent>(evt => OnWindowDefPortMenu(node, def, evt));
                }
            }

            BuildEdges();
            UpdateStatus();
            QueueRoute(0.05f);
        }

        /// <summary>
        /// Reverse index of every edge, so a scene that is only a target still explains
        /// who points at it instead of looking like an empty card.
        /// </summary>
        private Dictionary<string, List<SceneIncomingRef>> CollectIncoming()
        {
            var map = new Dictionary<string, List<SceneIncomingRef>>(StringComparer.OrdinalIgnoreCase);

            foreach (var scene in _scenes)
            {
                foreach (var source in scene.sources)
                {
                    if (string.IsNullOrEmpty(source.targetScenePath)) continue;
                    GetIncomingList(map, source.targetScenePath).Add(new SceneIncomingRef
                    {
                        sceneName = scene.sceneName,
                        displayName = source.displayName,
                        kind = source.kind,
                        mode = source.mode
                    });
                }

                foreach (var def in scene.windowDefs)
                {
                    if (string.IsNullOrEmpty(def.targetScenePath)) continue;
                    GetIncomingList(map, def.targetScenePath).Add(new SceneIncomingRef
                    {
                        sceneName = scene.sceneName,
                        displayName = "窗口配置 " + def.id,
                        kind = "WindowDatabase",
                        mode = def.isJump ? SceneGraphModes.Jump : SceneGraphModes.Async
                    });
                }
            }

            foreach (var list in map.Values)
                list.Sort((a, b) =>
                {
                    int byScene = string.Compare(a.sceneName, b.sceneName, StringComparison.OrdinalIgnoreCase);
                    return byScene != 0
                        ? byScene
                        : string.Compare(a.displayName, b.displayName, StringComparison.OrdinalIgnoreCase);
                });

            return map;
        }

        private static List<SceneIncomingRef> GetIncomingList(
            Dictionary<string, List<SceneIncomingRef>> map, string scenePath)
        {
            if (map.TryGetValue(scenePath, out var list)) return list;
            list = new List<SceneIncomingRef>();
            map[scenePath] = list;
            return list;
        }

        private void BuildEdges()
        {
            int count = 0;
            foreach (var scene in _scenes)
            {
                if (!_nodes.TryGetValue(scene.scenePath, out var sourceNode)) continue;

                for (int i = 0; i < scene.sources.Count && i < sourceNode.SourcePorts.Count; i++)
                {
                    if (string.IsNullOrEmpty(scene.sources[i].targetScenePath)) continue;
                    if (!_nodes.TryGetValue(scene.sources[i].targetScenePath, out var targetNode)) continue;

                    var edge = sourceNode.SourcePorts[i].ConnectTo(targetNode.InputPort);
                    if (edge == null) continue;
                    edge.userData = scene.sources[i];
                    _graph.AddElement(edge);

                    string key = SceneGraphEdgeKey.ForSource(scene.scenePath, scene.sources[i]);
                    RegisterEdge(edge, key);
                    SceneGraphEdgeRouter.Apply(edge,
                        ResolveEdgeColor(key, SceneGraphPalette.ForMode(scene.sources[i].mode)),
                        target => RegisterEdgeMenu(edge, key, target));
                    count++;
                }

                for (int i = 0; i < scene.windowDefs.Count && i < sourceNode.WindowDefPorts.Count; i++)
                {
                    if (string.IsNullOrEmpty(scene.windowDefs[i].targetScenePath)) continue;
                    if (!_nodes.TryGetValue(scene.windowDefs[i].targetScenePath, out var targetNode)) continue;

                    var edge = sourceNode.WindowDefPorts[i].ConnectTo(targetNode.InputPort);
                    if (edge == null) continue;
                    edge.userData = scene.windowDefs[i];
                    _graph.AddElement(edge);

                    string key = SceneGraphEdgeKey.ForWindow(scene.scenePath, scene.windowDefs[i].id);
                    RegisterEdge(edge, key);
                    SceneGraphEdgeRouter.Apply(edge,
                        ResolveEdgeColor(key, SceneGraphPalette.ForWindowDef(scene.windowDefs[i].isJump)),
                        target => RegisterEdgeMenu(edge, key, target));
                    count++;
                }
            }

            _status.text = $"{_scenes.Count} 场景 · {count} 连线 · " +
                           $"{_scenes.Sum(scene => scene.diagnostics.Count(item => item.level == "error"))} 错误";
        }

        private void RegisterEdge(Edge edge, string key)
        {
            _edgeKeys[edge] = key;
            _edges.Add(edge);

            if (!_edgesByKey.TryGetValue(key, out var list))
            {
                list = new List<Edge>();
                _edgesByKey[key] = list;
            }
            list.Add(edge);
        }

        /// <summary>The edge band lives in the edge container, so the menu is registered there.</summary>
        private void RegisterEdgeMenu(Edge edge, string key, VisualElement target)
        {
            target.RegisterCallback<ContextualMenuPopulateEvent>(evt => OnEdgeMenu(edge, key, evt));
        }

        private void OnEdgeMenu(Edge edge, string key, ContextualMenuPopulateEvent evt)
        {
            bool custom = _customColors.ContainsKey(key);

            // 连接本身的操作：改目标 / 异步跳转 / 删除，都按连线的源对象定位。
            AppendEdgeConnectionActions(edge, evt);

            evt.menu.AppendSeparator();
            evt.menu.AppendAction("自定义连线颜色…", _ => OpenEdgeColorPicker(key));
            evt.menu.AppendAction("恢复默认颜色（按类型）", _ =>
            {
                StoreCustomColor(key, null);
                RebuildFromModel();
            }, _ => custom ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);

            evt.menu.AppendAction("复制颜色值", _ =>
            {
                Color current = _customColors.TryGetValue(key, out var picked)
                    ? picked
                    : edge.edgeControl != null ? edge.edgeControl.inputColor : Color.white;
                EditorGUIUtility.systemCopyBuffer = SceneGraphColorPickerWindow.FormatHex(current);
            });

            evt.StopPropagation();
        }

        /// <summary>连线右键里的“改这条连接”动作：切换异步/跳转、清除（= 删除这条连接）。</summary>
        private void AppendEdgeConnectionActions(Edge edge, ContextualMenuPopulateEvent evt)
        {
            if (edge?.output == null || !_portOwners.TryGetValue(edge.output, out var node) || node == null)
                return;

            if (_sourcePorts.TryGetValue(edge.output, out var source) && source != null)
            {
                bool readOnly = false;                 // Button.onClick 现在也支持删除
                bool canRetarget = source.kind == SceneGraphSourceKinds.MenuButton;

                evt.menu.AppendAction("切换为 异步窗口", _ => SwitchMode(node, source, false),
                    _ => canRetarget ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                evt.menu.AppendAction("切换为 跳转场景", _ => SwitchMode(node, source, true),
                    _ => canRetarget ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);

                evt.menu.AppendAction("删除这条连接", _ => DisconnectEdge(edge),
                    _ => readOnly ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);

                // 「点击结束」事件也在画这条线时，提供一次连事件一起断的选项。
                if (source.kind == SceneGraphSourceKinds.MenuButton && !string.IsNullOrEmpty(source.eventTarget))
                    evt.menu.AppendAction("删除这条连接（含清除「点击结束」事件）",
                        _ => DisconnectEdge(edge, alsoClearEvent: true));

                evt.menu.AppendSeparator();
                evt.menu.AppendAction("定位源对象", _ => PingObject(node.Scene.scenePath, source.objectPath));
                evt.menu.AppendAction("复制窗口 ID", _ => EditorGUIUtility.systemCopyBuffer = source.windowId ?? string.Empty);
                if (readOnly)
                    evt.menu.AppendAction("只读：Button.onClick 事件", _ => { },
                        _ => DropdownMenuAction.Status.Disabled);
                return;
            }

            if (_defPorts.TryGetValue(edge.output, out var def) && def != null)
            {
                evt.menu.AppendAction("删除这条连接", _ => DisconnectEdge(edge));
                evt.menu.AppendSeparator();
                evt.menu.AppendAction("定位数据库", _ => PingObject(node.Scene.scenePath, def.objectPath));
                evt.menu.AppendAction("复制窗口 ID", _ => EditorGUIUtility.systemCopyBuffer = def.id);
            }
        }

        /// <summary>删除一条连线：能写回就写回，写不了至少把画面上的边去掉。</summary>
        private void DisconnectEdge(Edge edge, bool alsoClearEvent = false)
        {
            if (edge?.output != null && _portOwners.TryGetValue(edge.output, out var node) && node != null &&
                _sourcePorts.TryGetValue(edge.output, out var source) && source != null)
            {
                Disconnect(node, source, alsoClearEvent);
                return;
            }

            if (edge?.output != null && _defPorts.TryGetValue(edge.output, out var def) && def != null)
            {
                if (!_portOwners.TryGetValue(edge.output, out var defNode) || defNode == null) return;

                if (SceneGraphWriter.DisconnectWindowDef(def, defNode.Scene.scenePath, out var defMessage))
                {
                    ShowNotification(new GUIContent(defMessage));

                    // 同样只改模型 + 重建视图，避免全量重扫。
                    defNode.Scene.windowDefs.Remove(def);
                    RebuildFromModel();
                }
                else
                {
                    ShowNotification(new GUIContent(defMessage));
                    Debug.LogWarning("[SceneGraph] " + defMessage);
                }
                return;
            }

            ShowNotification(new GUIContent("这条连线无法写回，已仅从画面移除"));
            if (edge != null && edge.parent != null) _graph.RemoveElement(edge);
        }

        private void OpenEdgeColorPicker(string key)
        {
            Color current = _customColors.TryGetValue(key, out var picked)
                ? picked
                : FallbackEdgeColor(key);

            SceneGraphColorPickerWindow.Open(
                "连线颜色",
                current,
                color => ApplyColorToEdges(key, color),
                color =>
                {
                    StoreCustomColor(key, color);
                    ApplyColorToEdges(key, color);
                },
                () =>
                {
                    StoreCustomColor(key, null);
                    RebuildFromModel();
                });
        }

        private Color FallbackEdgeColor(string key)
        {
            foreach (var scene in _scenes)
            {
                foreach (var source in scene.sources)
                {
                    if (!string.Equals(SceneGraphEdgeKey.ForSource(scene.scenePath, source), key,
                            StringComparison.OrdinalIgnoreCase)) continue;
                    return SceneGraphPalette.ForMode(source.mode);
                }

                foreach (var def in scene.windowDefs)
                {
                    if (!string.Equals(SceneGraphEdgeKey.ForWindow(scene.scenePath, def.id), key,
                            StringComparison.OrdinalIgnoreCase)) continue;
                    return SceneGraphPalette.ForWindowDef(def.isJump);
                }
            }

            return SceneGraphPalette.Unresolved;
        }

        private Color ResolveEdgeColor(string key, Color fallback)
            => _customColors.TryGetValue(key, out var custom) ? custom : fallback;

        private void ApplyColorToEdges(string key, Color color)
        {
            if (!_edgesByKey.TryGetValue(key, out var edges)) return;

            foreach (var edge in edges)
            {
                if (edge == null || edge.parent == null) continue;
                SceneGraphEdgeRouter.Apply(edge, color);
                edge.edgeControl?.MarkDirtyRepaint();
            }
        }

        private void LoadCustomColors()
        {
            _customColors.Clear();
            if (_layout?.edgeColors == null) return;

            foreach (var entry in _layout.edgeColors)
            {
                if (entry == null || string.IsNullOrEmpty(entry.key)) continue;
                if (SceneGraphColorPickerWindow.TryParseHex(entry.color, out var color))
                    _customColors[entry.key] = color;
            }
        }

        private void StoreCustomColor(string key, Color? color)
        {
            if (_layout == null) return;

            _layout.edgeColors?.RemoveAll(entry => entry == null || entry.key == key);

            if (color.HasValue)
            {
                if (_layout.edgeColors == null) _layout.edgeColors = new List<SceneGraphEdgeColor>();
                _layout.edgeColors.Add(new SceneGraphEdgeColor
                {
                    key = key,
                    color = SceneGraphColorPickerWindow.FormatHex(color.Value)
                });
                _customColors[key] = color.Value;
            }
            else _customColors.Remove(key);

            QueueSaveLayout();
        }

        private void RebuildFromModel()
        {
            RebuildGraphView();
            BuildNodes();
        }

        private GraphViewChange OnGraphChanged(GraphViewChange change)
        {
            // 选中连线按 Delete：GraphView 只删视觉元素，这里把写回也一起做掉。
            if (change.elementsToRemove != null)
            {
                var removedEdges = change.elementsToRemove.OfType<Edge>().ToList();
                if (removedEdges.Count > 0)
                {
                    EditorApplication.delayCall += () =>
                    {
                        foreach (var edge in removedEdges) DisconnectEdge(edge);
                    };
                }
            }

            if (change.movedElements != null)
            {
                foreach (var node in change.movedElements.OfType<SceneCardNode>())
                {
                    var position = node.GetPosition();
                    node.Scene.x = position.x;
                    node.Scene.y = position.y;
                }
                QueueSaveLayout();
                QueueRoute();
            }
            return change;
        }

        private void AutoLayout()
        {
            ComputeAutoLayoutPositions();
            RebuildGraphView();
            BuildNodes();
            QueueSaveLayout();
        }

        private void ComputeAutoLayoutPositions()
        {
            const int columns = 4;
            const float width = 340f;
            // Wide gaps: connected cards need room between them or every edge has to squeeze
            // through the same narrow corridor and starts weaving.
            const float gapX = 150f;
            const float gapY = 60f;

            float x = 40f;
            float y = 40f;
            float rowHeight = 0f;
            int column = 0;

            foreach (var scene in _scenes.OrderBy(scene => scene.buildIndex).ThenBy(scene => scene.sceneName))
            {
                float height = 220f;
                if (_nodes.TryGetValue(scene.scenePath, out var node) && node != null)
                {
                    // GraphView decides the real height from content; the estimate can be off by
                    // enough to leave two cards overlapping, which then blocks edge routing.
                    float actual = node.layout.height;
                    height = Mathf.Max(120f, actual > 1f ? actual : node.PreferredHeight);
                }

                scene.x = x;
                scene.y = y;

                rowHeight = Mathf.Max(rowHeight, height);
                column++;
                if (column >= columns)
                {
                    column = 0;
                    x = 40f;
                    y += rowHeight + gapY;
                    rowHeight = 0f;
                }
                else x += width + gapX;
            }
        }

        private void SetAllCollapsed(bool collapsed)
        {
            foreach (var node in _nodes.Values)
                node.SetCollapsed(collapsed);
            QueueSaveLayout();
        }

        private void ToggleCollapse(SceneCardNode node)
        {
            node.SetCollapsed(!node.Scene.collapsed);
            QueueSaveLayout();
        }

        private void RequestScan()
        {
            if (_scanQueued) return;
            _scanQueued = true;
            EditorApplication.delayCall += () =>
            {
                _scanQueued = false;
                Scan();
            };
        }

        private void QueueSaveLayout()
        {
            if (_saveQueued) return;
            _saveQueued = true;
            EditorApplication.delayCall += () =>
            {
                _saveQueued = false;
                SaveLayout();
            };
        }

        /// <summary>
        /// Re-routing is heavy, so wait for a quiet moment (end of a drag) instead of running
        /// it on every node position change.
        /// </summary>
        private void QueueRoute(float delaySeconds = 0.25f)
        {
            _routeAt = EditorApplication.timeSinceStartup + delaySeconds;
            if (_routeTickHooked) return;

            _routeTickHooked = true;
            EditorApplication.update += RouteTick;
        }

        private void RouteTick()
        {
            if (EditorApplication.timeSinceStartup < _routeAt) return;

            EditorApplication.update -= RouteTick;
            _routeTickHooked = false;
            RouteEdgesNow();
        }

        private void RouteEdgesNow()
        {
            if (_graph == null || _edges.Count == 0) return;

            var rects = new List<Rect>(_nodes.Count);
            foreach (var node in _nodes.Values)
            {
                Rect rect = node.layout;
                if (rect.width <= 1f || rect.height <= 1f) rect = node.GetPosition();
                rects.Add(rect);
            }

            // The panel may not have laid the edges out yet on the first frame; routing then
            // would use (0,0) endpoints and persist a garbage route.
            bool ready = rects.Exists(rect => rect.width > 1f && rect.height > 1f);
            if (ready)
            {
                foreach (var edge in _edges)
                {
                    var control = edge.edgeControl;
                    if (control == null) continue;
                    if (float.IsNaN(control.from.x) || float.IsNaN(control.to.x) ||
                        (control.from == Vector2.zero && control.to == Vector2.zero))
                    {
                        ready = false;
                        break;
                    }
                }
            }

            if (!ready)
            {
                if (++_routeAttempts < 40) QueueRoute(0.1f);
                return;
            }

            _routeAttempts = 0;
            SceneGraphEdgeRouter.RouteAll(_edges, rects);

            foreach (var edge in _edges)
                edge.edgeControl?.MarkDirtyRepaint();
        }

        private void SaveLayout()
        {
            if (_layout == null) return;
            SceneGraphLayoutStore.Capture(_scenes, _layout);
            if (_miniMap != null)
            {
                var rect = _miniMap.GetPosition();
                _layout.miniX = rect.x;
                _layout.miniY = rect.y;
            }
            SceneGraphLayoutStore.Save(_layout);
        }

        private void SetupMiniMap(MiniMap miniMap)
        {
            if (miniMap == null) return;

            miniMap.style.minWidth = MiniMapWidth;
            miniMap.style.minHeight = MiniMapHeight;

            Vector2 moveStart = default;
            Rect moveRect = default;
            bool moving = false;

            miniMap.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 1 && !evt.altKey) return;
                moving = true;
                moveStart = evt.position;
                moveRect = miniMap.GetPosition();
                miniMap.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });
            miniMap.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!moving) return;
                Vector2 delta = (Vector2)evt.position - moveStart;
                miniMap.SetPosition(new Rect(moveRect.x + delta.x, moveRect.y + delta.y, MiniMapWidth, MiniMapHeight));
            });
            miniMap.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!moving) return;
                moving = false;
                miniMap.ReleasePointer(evt.pointerId);
                QueueSaveLayout();
            });
            miniMap.RegisterCallback<PointerCaptureOutEvent>(_ => moving = false);
        }

        private void BuildContextMenu(ContextualMenuPopulateEvent evt)
        {
            // Unity 只把 ContextualMenuPopulateEvent 发给 GraphView 自己，挂在卡片/端口/连线上的
            // 回调永远不会触发；所以按“最近一次指针位置”在这里分发到对应对象。
            if (AppendObjectMenu(evt)) return;

            evt.menu.AppendAction("扫描 / 刷新全部", _ => Scan());
            evt.menu.AppendAction("自动布局", _ => AutoLayout());
            evt.menu.AppendAction("保存布局", _ => SaveLayout());
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("全部展开", _ => SetAllCollapsed(false));
            evt.menu.AppendAction("全部折叠", _ => SetAllCollapsed(true));
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("打开布局文件", _ => EditorUtility.RevealInFinder(SceneGraphLayoutStore.FilePath));
        }

        /// <summary>指针下有对象时构造该对象的菜单；返回 true 表示已接管。</summary>
        private bool AppendObjectMenu(ContextualMenuPopulateEvent evt)
        {
            var edge = FindEdgeNearPointer();
            if (edge != null && _edgeKeys.TryGetValue(edge, out var edgeKey))
            {
                OnEdgeMenu(edge, edgeKey, evt);
                return true;
            }

            foreach (var pair in _sourcePorts)
            {
                if (!IsPointerNearPort(pair.Key)) continue;
                OnSourcePortMenu(_portOwners[pair.Key], pair.Value, evt);
                return true;
            }

            foreach (var pair in _defPorts)
            {
                if (!IsPointerNearPort(pair.Key)) continue;
                OnWindowDefPortMenu(_portOwners[pair.Key], pair.Value, evt);
                return true;
            }

            var node = FindNodeAt(PointerContentPosition());
            if (node == null) return false;

            OnNodeMenu(node, evt);
            return true;
        }

        private bool IsPointerNearPort(Port port)
        {
            if (port == null) return false;
            Rect bounds = port.worldBound;
            if (bounds.width <= 0f) return false;

            Vector2 center = bounds.center;
            Vector2 point = _pointerPanelPosition;
            return (point - center).sqrMagnitude <= 14f * 14f;
        }

        private SceneCardNode FindNodeAt(Vector2 contentPoint)
        {
            foreach (var node in _nodes.Values)
            {
                Rect rect = node.layout;
                if (rect.width > 1f && rect.Contains(contentPoint)) return node;
            }
            return null;
        }

        /// <summary>把屏幕/面板坐标换算成内容坐标后找卡片（两种坐标都试，兼容不同版本的拖拽回调）。</summary>
        private bool TryFindNode(Vector2 position, out SceneCardNode node)
        {
            var container = _edges.Count > 0 ? _edges[0].parent : null;
            if (container != null)
            {
                node = FindNodeAt(container.WorldToLocal(position));
                if (node != null) return true;
            }

            node = FindNodeAt(position);
            return node != null;
        }

        /// <summary>Converts the last pointer position into graph content space.</summary>
        private Vector2 PointerContentPosition()
        {
            // Routes and node rects are expressed in the edge container's local space.
            var container = _edges.Count > 0 ? _edges[0].parent : null;
            if (container == null)
            {
                foreach (var node in _nodes.Values)
                    container = node.parent;
                if (container == null) return _pointerPanelPosition;
            }

            return container.WorldToLocal(_pointerPanelPosition);
        }

        private Edge FindEdgeNearPointer()
        {
            if (_edges.Count == 0) return null;

            Vector2 content = PointerContentPosition();
            const float threshold = 14f;

            Edge best = null;
            float bestDistance = threshold;

            foreach (var edge in _edges)
            {
                float distance = SceneGraphEdgeRouter.DistanceToRoute(edge, content);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = edge;
            }

            return best;
        }

        private void OnNodeMenu(SceneCardNode node, ContextualMenuPopulateEvent evt)
        {
            var scene = node.Scene;
            evt.menu.AppendAction("打开场景", _ => OpenScene(scene.scenePath, false));
            evt.menu.AppendAction("以 Additive 打开", _ => OpenScene(scene.scenePath, true));
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("折叠 / 展开", _ => ToggleCollapse(node));
            evt.menu.AppendAction("刷新此场景", _ => Scan());
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("在 Project 中定位场景资产",
                _ => EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.scenePath)));
            evt.menu.AppendAction("复制场景路径", _ => EditorGUIUtility.systemCopyBuffer = scene.scenePath);
            evt.StopPropagation();
        }

        private void OnSourcePortMenu(SceneCardNode node, SceneGraphSource source, ContextualMenuPopulateEvent evt)
        {
            bool editable = source.kind != SceneGraphSourceKinds.ButtonEvent;

            if (editable)
            {
                evt.menu.AppendAction("切换为 异步窗口", _ => SwitchMode(node, source, false),
                    _ => string.IsNullOrEmpty(source.targetScenePath) ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
                evt.menu.AppendAction("切换为 跳转场景", _ => SwitchMode(node, source, true),
                    _ => string.IsNullOrEmpty(source.targetScenePath) ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
                evt.menu.AppendAction("清除连接", _ => Disconnect(node, source));
                evt.menu.AppendSeparator();
            }

            evt.menu.AppendAction("连线颜色…", _ => OpenEdgeColorPicker(
                    SceneGraphEdgeKey.ForSource(node.Scene.scenePath, source)),
                _ => string.IsNullOrEmpty(source.targetScenePath)
                    ? DropdownMenuAction.Status.Disabled
                    : DropdownMenuAction.Status.Normal);
            evt.menu.AppendAction("复制窗口 ID", _ => EditorGUIUtility.systemCopyBuffer = source.windowId ?? string.Empty);
            evt.menu.AppendAction("在场景中定位", _ => PingObject(node.Scene.scenePath, source.objectPath));

            if (!editable)
                evt.menu.AppendAction("只读：Button.onClick 事件", _ => { },
                    _ => DropdownMenuAction.Status.Disabled);

            evt.StopPropagation();
        }

        private void OnWindowDefPortMenu(SceneCardNode node, SceneGraphWindowDef def, ContextualMenuPopulateEvent evt)
        {
            evt.menu.AppendAction("连线颜色…", _ => OpenEdgeColorPicker(
                    SceneGraphEdgeKey.ForWindow(node.Scene.scenePath, def.id)),
                _ => string.IsNullOrEmpty(def.targetScenePath)
                    ? DropdownMenuAction.Status.Disabled
                    : DropdownMenuAction.Status.Normal);
            evt.menu.AppendAction("复制窗口 ID", _ => EditorGUIUtility.systemCopyBuffer = def.id);
            evt.menu.AppendAction("在场景中定位", _ => PingObject(node.Scene.scenePath, def.objectPath));
            evt.StopPropagation();
        }

        private void SwitchMode(SceneCardNode node, SceneGraphSource source, bool isJump)
        {
            if (string.IsNullOrEmpty(source.targetScenePath))
            {
                ShowNotification(new GUIContent("先拖线到目标场景"));
                return;
            }

            if (SceneGraphWriter.Connect(source, node.Scene.scenePath, source.targetScenePath, isJump, out var message))
            {
                ShowNotification(new GUIContent(message));
                RequestScan();
            }
            else
            {
                ShowNotification(new GUIContent(message));
                Debug.LogWarning("[SceneGraph] " + message);
            }
        }

        private void Disconnect(SceneCardNode node, SceneGraphSource source, bool alsoClearEvent = false)
        {
            if (!SceneGraphWriter.Disconnect(source, node.Scene.scenePath, out var message, alsoClearEvent))
            {
                ShowNotification(new GUIContent(message));
                Debug.LogWarning("[SceneGraph] " + message);
                return;
            }

            ShowNotification(new GUIContent(message));

            // 只更新内存模型再重建视图：不再全量重扫全部 Build 场景（否则会整板重排、弹导入/保存窗口）。
            source.mode = SceneGraphModes.None;
            source.targetSceneName = null;
            source.targetScenePath = null;
            source.resolved = false;
            source.windowId = null;
            if (alsoClearEvent) source.eventTarget = null;

            RebuildFromModel();
        }

        private static void PingObject(string scenePath, string objectPath)
        {
            if (string.IsNullOrEmpty(scenePath) || string.IsNullOrEmpty(objectPath)) return;

            var scene = SceneManager.GetSceneByPath(scenePath);
            if (!scene.IsValid() || !scene.isLoaded)
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (SceneGraphScanner.HierarchyPath(transform) != objectPath) continue;
                    Selection.activeGameObject = transform.gameObject;
                    SceneView.lastActiveSceneView?.FrameSelected();
                    return;
                }
            }

            Debug.LogWarning("[SceneGraph] 找不到对象：" + objectPath);
        }

        private static void OpenScene(string scenePath, bool additive)
        {
            if (string.IsNullOrEmpty(scenePath) || !System.IO.File.Exists(scenePath)) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(scenePath, additive ? OpenSceneMode.Additive : OpenSceneMode.Single);
        }

        private void UpdateStatus()
        {
            if (_status == null) return;
            int errors = _scenes.Sum(scene => scene.diagnostics.Count(item => item.level == "error"));
            int warnings = _scenes.Sum(scene => scene.diagnostics.Count(item => item.level == "warning"));
            _status.text = $"{_scenes.Count} 场景 · {_nodes.Count} 卡片 · {errors} 错误 · {warnings} 警告";
        }

        private sealed class SceneGraphEdgeListener : IEdgeConnectorListener
        {
            private readonly SceneGraphWindow _window;

            public SceneGraphEdgeListener(SceneGraphWindow window) => _window = window;

            public void OnDropOutsidePort(Edge edge, Vector2 position)
            {
                // 落在卡片主体（而不是那个 12px 圆点）时也算连到这张卡。
                if (_window.TryConnectOnCard(edge, position)) return;
                _window.ShowNotification(new GUIContent("请拖到目标场景卡片"));
            }

            public void OnDrop(GraphView graphView, Edge edge) => _window.HandleEdgeDrop(graphView, edge);
        }

        private static void RemoveEdgeLater(GraphView graphView, Edge edge)
        {
            if (graphView == null || edge == null) return;
            EditorApplication.delayCall += () =>
            {
                if (graphView != null && edge.parent != null)
                    graphView.RemoveElement(edge);
            };
        }

        private void HandleEdgeDrop(GraphView graphView, Edge edge)
        {
            if (graphView == null || edge == null || edge.output == null || edge.input == null)
            {
                RemoveEdgeLater(graphView, edge);
                return;
            }

            if (!_portOwners.TryGetValue(edge.input, out var target) || target == null ||
                !_portOwners.TryGetValue(edge.output, out var sourceNode) || sourceNode == null)
            {
                RemoveEdgeLater(graphView, edge);
                return;
            }

            if (!ConnectSourceTo(edge, target)) RemoveEdgeLater(graphView, edge);
        }

        /// <summary>拖到卡片任意位置（不一定非要点中入端口圆点）时的写回。</summary>
        public bool TryConnectOnCard(Edge edge, Vector2 position)
        {
            if (edge == null || edge.output == null) return false;
            if (!TryFindNode(position, out var target) || target == null) return false;

            if (_portOwners.TryGetValue(edge.output, out var owner) && owner == target) return false;
            return ConnectSourceTo(edge, target);
        }

        /// <summary>把某条源（按钮行 / 窗口定义行）连到目标场景卡片，成功返回 true。</summary>
        private bool ConnectSourceTo(Edge edge, SceneCardNode target)
        {
            if (edge?.output == null || target == null) return false;
            if (!_portOwners.TryGetValue(edge.output, out var sourceNode) || sourceNode == null) return false;

            bool ok;
            string message;
            if (_sourcePorts.TryGetValue(edge.output, out var source))
                ok = SceneGraphWriter.Connect(source, sourceNode.Scene.scenePath, target.Scene.scenePath, false, out message);
            else if (_defPorts.TryGetValue(edge.output, out var def))
                ok = SceneGraphWriter.ConnectWindowDef(def, sourceNode.Scene.scenePath, target.Scene.scenePath, out message);
            else return false;

            ShowNotification(new GUIContent(message));
            if (ok)
            {
                Debug.Log("[SceneGraph] " + message);
                RequestScan();
            }
            else Debug.LogWarning("[SceneGraph] " + message);

            return ok;
        }

        /// <summary>撤销/重做后场景数据变了，需要重新扫描并重建卡片。</summary>
        private void OnUndoRedo()
        {
            if (this == null) return;
            RequestScan();
        }

        /// <summary>Ctrl+Z 撤销、Ctrl+Shift+Z / Ctrl+Y 重做（GraphView 会吞掉按键，这里显式处理）。</summary>
        private void OnKeyDown(KeyDownEvent evt)
        {
            if (!evt.ctrlKey && !evt.commandKey) return;

            if (evt.keyCode == KeyCode.Z)
            {
                if (evt.shiftKey) Undo.PerformRedo();
                else Undo.PerformUndo();
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.Y)
            {
                Undo.PerformRedo();
                evt.StopPropagation();
            }
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;

            if (_routeTickHooked)
            {
                EditorApplication.update -= RouteTick;
                _routeTickHooked = false;
            }

            if (_layout != null && _scenes.Count > 0) SaveLayout();
        }
    }
}
