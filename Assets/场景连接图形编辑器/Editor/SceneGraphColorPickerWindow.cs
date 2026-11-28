using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace Lament.SceneGraph
{
    /// <summary>
    /// Photoshop-like color wheel: hue ring + saturation/value square with a hex field
    /// underneath, used to give a single connection its own color.
    /// </summary>
    public sealed class SceneGraphColorPickerWindow : EditorWindow
    {
        private const float WheelSize = 240f;
        private const float OuterRadius = 116f;
        private const float InnerRadius = 90f;
        private const int RingTextureSize = 256;
        private const int SvTextureSize = 128;
        private const float WindowWidth = 260f;
        private const float WindowHeight = 320f;

        private static Texture2D _ringTexture;

        private Color _color = Color.white;
        private float _hue;
        private float _saturation = 1f;
        private float _value = 1f;
        private string _hexText = "#FFFFFF";
        private bool _syncingHex;

        private Texture2D _svTexture;
        private float _svHue = -1f;

        private bool _draggingRing;
        private bool _draggingSv;

        private Action<Color> _onChanged;
        private Action<Color> _onCommitted;
        private Action _onReset;

        /// <param name="title">Window title.</param>
        /// <param name="current">Color to start from.</param>
        /// <param name="onChanged">Called while dragging, for live preview.</param>
        /// <param name="onCommitted">Called when the user confirms or closes the window.</param>
        /// <param name="onReset">Clears the custom color; shown as "恢复默认".</param>
        public static void Open(string title, Color current,
            Action<Color> onChanged, Action<Color> onCommitted, Action onReset = null)
        {
            var window = CreateInstance<SceneGraphColorPickerWindow>();
            window.titleContent = new GUIContent(title);
            window.minSize = new Vector2(WindowWidth, WindowHeight);
            window.maxSize = new Vector2(WindowWidth, WindowHeight);

            var main = EditorGUIUtility.GetMainWindowPosition();
            window.position = new Rect(
                main.center.x - WindowWidth * 0.5f,
                main.center.y - WindowHeight * 0.5f,
                WindowWidth,
                WindowHeight);

            window._onChanged = onChanged;
            window._onCommitted = onCommitted;
            window._onReset = onReset;
            window.SetColor(current, true);

            window.ShowUtility();
            window.Repaint();
        }

        private void SetColor(Color color, bool syncHex)
        {
            color.a = 1f;
            _color = color;
            Color.RGBToHSV(color, out _hue, out _saturation, out _value);
            if (syncHex) _hexText = ToHex(color);
            RebuildSvTexture();
            Repaint();
        }

        private void OnDisable()
        {
            if (_onCommitted != null) _onCommitted(_color);
            _onCommitted = null;
            _onChanged = null;
            _onReset = null;

            if (_svTexture != null)
            {
                DestroyImmediate(_svTexture);
                _svTexture = null;
            }
        }

        private void OnGUI()
        {
            if (_ringTexture == null) _ringTexture = BuildRingTexture();
            if (_svTexture == null) RebuildSvTexture();

            var wheelRect = new Rect(10f, 10f, WheelSize, WheelSize);
            GUI.DrawTexture(wheelRect, _ringTexture, ScaleMode.StretchToFill);

            var svRect = new Rect(
                wheelRect.center.x - SvTextureSize * 0.5f,
                wheelRect.center.y - SvTextureSize * 0.5f,
                SvTextureSize,
                SvTextureSize);
            GUI.DrawTexture(svRect, _svTexture, ScaleMode.StretchToFill);

            DrawRingCursor(wheelRect);
            DrawSvCursor(svRect);
            HandleWheelInput(wheelRect, svRect);

            const float rowY = 258f;
            GUI.Label(new Rect(10f, rowY, 34f, 18f), "HEX");

            EditorGUI.BeginChangeCheck();
            string typed = EditorGUI.TextField(new Rect(46f, rowY, 120f, 18f), _hexText);
            if (EditorGUI.EndChangeCheck() && !_syncingHex)
            {
                _hexText = typed;
                if (TryParseHex(typed, out var parsed))
                {
                    Color.RGBToHSV(parsed, out _hue, out _saturation, out _value);
                    _color = parsed;
                    RebuildSvTexture();
                    NotifyChanged();
                }
            }

            var swatch = new Rect(174f, rowY, 34f, 18f);
            EditorGUI.DrawRect(swatch, _color);
            Handles.color = new Color(0f, 0f, 0f, 0.5f);
            Handles.DrawAAPolyLine(1f,
                new Vector3(swatch.x, swatch.y), new Vector3(swatch.xMax, swatch.y),
                new Vector3(swatch.xMax, swatch.yMax), new Vector3(swatch.x, swatch.yMax),
                new Vector3(swatch.x, swatch.y));

            const float buttonY = 286f;
            if (_onReset != null &&
                GUI.Button(new Rect(10f, buttonY, 76f, 22f), "恢复默认"))
            {
                _onReset();
                Close();
                return;
            }

            if (GUI.Button(new Rect(94f, buttonY, 74f, 22f), "取消")) Close();
            if (GUI.Button(new Rect(174f, buttonY, 76f, 22f), "确定"))
            {
                if (_onCommitted != null) _onCommitted(_color);
                _onCommitted = null;
                Close();
            }

            UpdateHexPreview();
        }

        private void UpdateHexPreview()
        {
            string expected = ToHex(_color);
            if (string.Equals(expected, _hexText, StringComparison.OrdinalIgnoreCase)) return;

            // Keep the field in sync while dragging; never fight the user's typing.
            _syncingHex = true;
            _hexText = expected;
            _syncingHex = false;
            Repaint();
        }

        private void NotifyChanged()
        {
            if (_onChanged != null) _onChanged(_color);
            Repaint();
        }

        private void HandleWheelInput(Rect wheelRect, Rect svRect)
        {
            var current = Event.current;
            if (current == null) return;

            bool down = current.type == EventType.MouseDown && current.button == 0;
            bool drag = current.type == EventType.MouseDrag && current.button == 0;
            bool up = current.type == EventType.MouseUp;

            if (up)
            {
                _draggingRing = false;
                _draggingSv = false;
                return;
            }

            if (!down && !drag) return;

            Vector2 point = current.mousePosition;

            if (down)
            {
                float distance = Vector2.Distance(point, wheelRect.center);
                bool inRing = distance <= OuterRadius + 4f && distance >= InnerRadius - 4f;
                _draggingRing = inRing;
                _draggingSv = svRect.Contains(point);
                if (!_draggingRing && !_draggingSv) return;
            }

            if (_draggingRing)
            {
                Vector2 offset = point - wheelRect.center;
                if (offset.sqrMagnitude > 0.01f)
                {
                    // Hue 0 starts at the top and runs clockwise, like a classic color wheel.
                    float angle = Mathf.Atan2(offset.x, offset.y);
                    _hue = Mathf.Repeat(angle / (Mathf.PI * 2f), 1f);
                    ApplyHsv();
                    current.Use();
                }
            }
            else if (_draggingSv)
            {
                _saturation = Mathf.Clamp01((point.x - svRect.x) / svRect.width);
                _value = 1f - Mathf.Clamp01((point.y - svRect.y) / svRect.height);
                ApplyHsv();
                current.Use();
            }
        }

        private void ApplyHsv()
        {
            _color = Color.HSVToRGB(_hue, _saturation, _value);
            _color.a = 1f;
            RebuildSvTexture();
            _hexText = ToHex(_color);
            NotifyChanged();
        }

        private void DrawRingCursor(Rect wheelRect)
        {
            float angle = _hue * Mathf.PI * 2f;
            var direction = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
            const float radius = (OuterRadius + InnerRadius) * 0.5f;
            Vector2 center = wheelRect.center + direction * radius;
            DrawCircleOutline(center, 6f, Color.black);
            DrawCircleOutline(center, 5f, Color.white);
        }

        private void DrawSvCursor(Rect svRect)
        {
            var point = new Vector2(
                svRect.x + _saturation * svRect.width,
                svRect.y + (1f - _value) * svRect.height);
            DrawCircleOutline(point, 6f, Color.black);
            DrawCircleOutline(point, 5f, Color.white);
        }

        private static void DrawCircleOutline(Vector2 center, float radius, Color color)
        {
            Handles.color = color;
            Handles.DrawWireDisc(new Vector3(center.x, center.y, 0f), Vector3.forward, radius);
        }

        private void RebuildSvTexture()
        {
            if (_svTexture == null)
            {
                _svTexture = new Texture2D(SvTextureSize, SvTextureSize, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                _svHue = -1f;
            }

            if (Mathf.Abs(_svHue - _hue) < 0.0005f) return;
            _svHue = _hue;

            var pixels = new Color[SvTextureSize * SvTextureSize];
            for (int y = 0; y < SvTextureSize; y++)
            {
                float v = 1f - y / (SvTextureSize - 1f);
                for (int x = 0; x < SvTextureSize; x++)
                {
                    float s = x / (SvTextureSize - 1f);
                    pixels[y * SvTextureSize + x] = Color.HSVToRGB(_hue, s, v);
                }
            }

            _svTexture.SetPixels(pixels);
            _svTexture.Apply(false);
        }

        private static Texture2D BuildRingTexture()
        {
            var texture = new Texture2D(RingTextureSize, RingTextureSize, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            float half = RingTextureSize * 0.5f;
            float scale = WheelSize / RingTextureSize;
            float outer = OuterRadius / scale;
            float inner = InnerRadius / scale;

            var pixels = new Color[RingTextureSize * RingTextureSize];
            for (int y = 0; y < RingTextureSize; y++)
            {
                for (int x = 0; x < RingTextureSize; x++)
                {
                    float dx = x + 0.5f - half;
                    float dy = y + 0.5f - half;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    if (distance > outer || distance < inner)
                    {
                        pixels[y * RingTextureSize + x] = new Color(0f, 0f, 0f, 0f);
                        continue;
                    }

                    float angle = Mathf.Atan2(dx, -dy);
                    float hue = Mathf.Repeat(angle / (Mathf.PI * 2f), 1f);
                    var color = Color.HSVToRGB(hue, 1f, 1f);
                    color.a = 1f;
                    pixels[y * RingTextureSize + x] = color;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false);
            return texture;
        }

        private static string ToHex(Color color)
        {
            int r = Mathf.RoundToInt(Mathf.Clamp01(color.r) * 255f);
            int g = Mathf.RoundToInt(Mathf.Clamp01(color.g) * 255f);
            int b = Mathf.RoundToInt(Mathf.Clamp01(color.b) * 255f);
            return "#" + r.ToString("X2", CultureInfo.InvariantCulture)
                       + g.ToString("X2", CultureInfo.InvariantCulture)
                       + b.ToString("X2", CultureInfo.InvariantCulture);
        }

        public static bool TryParseHex(string text, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrEmpty(text)) return false;

            string value = text.Trim();
            if (value.StartsWith("#", StringComparison.Ordinal)) value = value.Substring(1);
            if (value.Length == 3)
                value = new string(new[] { value[0], value[0], value[1], value[1], value[2], value[2] });
            if (value.Length != 6) return false;

            if (!int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int packed))
                return false;

            color = new Color(
                ((packed >> 16) & 0xFF) / 255f,
                ((packed >> 8) & 0xFF) / 255f,
                (packed & 0xFF) / 255f,
                1f);
            return true;
        }

        public static string FormatHex(Color color) => ToHex(color);
    }
}
