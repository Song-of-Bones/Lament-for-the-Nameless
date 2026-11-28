using UnityEngine;
using UnityEngine.UIElements;

namespace Lament.SceneGraph
{
    /// <summary>
    /// One color per connection kind. Card rows, ports, edges and the toolbar legend all read
    /// from here, so a color always means the same thing across the graph.
    /// </summary>
    public static class SceneGraphPalette
    {
        public static readonly Color Async = new Color(0.30f, 0.66f, 1f);
        public static readonly Color Jump = new Color(1f, 0.65f, 0.24f);
        public static readonly Color Direct = new Color(0.35f, 0.82f, 0.46f);
        public static readonly Color Event = new Color(0.76f, 0.55f, 0.96f);
        public static readonly Color Unresolved = new Color(0.52f, 0.55f, 0.58f);
        public static readonly Color Incoming = new Color(0.72f, 0.76f, 0.82f);

        public static Color ForMode(string mode)
        {
            switch (mode)
            {
                case SceneGraphModes.Async: return Async;
                case SceneGraphModes.Jump: return Jump;
                case SceneGraphModes.Direct: return Direct;
                case SceneGraphModes.Event: return Event;
                default: return Unresolved;
            }
        }

        public static string LabelForMode(string mode)
        {
            switch (mode)
            {
                case SceneGraphModes.Async: return "异步";
                case SceneGraphModes.Jump: return "跳转";
                case SceneGraphModes.Direct: return "直接";
                case SceneGraphModes.Event: return "事件";
                default: return "未连接";
            }
        }

        public static Color ForWindowDef(bool isJump) => isJump ? Jump : Async;

        public static VisualElement Swatch(Color color, float size)
        {
            var dot = new VisualElement();
            dot.pickingMode = PickingMode.Ignore;
            dot.style.width = size;
            dot.style.height = size;
            dot.style.flexShrink = 0;
            dot.style.backgroundColor = color;
            dot.style.borderTopLeftRadius = size * 0.5f;
            dot.style.borderTopRightRadius = size * 0.5f;
            dot.style.borderBottomLeftRadius = size * 0.5f;
            dot.style.borderBottomRightRadius = size * 0.5f;
            return dot;
        }

        public static VisualElement LegendItem(Color color, string text)
        {
            var item = new VisualElement();
            item.style.flexDirection = FlexDirection.Row;
            item.style.alignItems = Align.Center;
            item.style.marginRight = 12;

            var dot = Swatch(color, 8f);
            dot.style.marginRight = 4;
            item.Add(dot);

            var label = new Label(text);
            label.style.fontSize = 10;
            label.style.color = new Color(0.78f, 0.80f, 0.83f);
            item.Add(label);
            return item;
        }
    }
}
