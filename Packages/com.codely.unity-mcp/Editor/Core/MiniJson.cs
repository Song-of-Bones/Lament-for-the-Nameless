using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Codely.UnityMcp
{
    /// <summary>
    /// Minimal dependency-free JSON reader/writer used by the bridge wire protocol.
    /// Values map to: Dictionary&lt;string,object&gt;, List&lt;object&gt;, string, double,
    /// bool and null. Deliberately small: the bridge must not depend on Newtonsoft or
    /// System.Text.Json, neither of which is guaranteed to be present in every editor.
    /// </summary>
    public static class MiniJson
    {
        public static object Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var parser = new Parser(json);
            object value = parser.ParseValue();
            parser.SkipWhitespace();
            if (!parser.AtEnd) throw new FormatException($"Unexpected trailing content at position {parser.Position}.");
            return value;
        }

        public static string Serialize(object value)
        {
            var sb = new StringBuilder(256);
            WriteValue(sb, value);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object value)
        {
            if (value == null) { sb.Append("null"); return; }

            if (value is string s) { WriteString(sb, s); return; }
            if (value is bool b) { sb.Append(b ? "true" : "false"); return; }

            if (value is IDictionary<string, object> dict)
            {
                sb.Append('{');
                bool first = true;
                foreach (var kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, kv.Key);
                    sb.Append(':');
                    WriteValue(sb, kv.Value);
                }
                sb.Append('}');
                return;
            }

            if (value is IDictionary looseDict)
            {
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry entry in looseDict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, Convert.ToString(entry.Key, CultureInfo.InvariantCulture));
                    sb.Append(':');
                    WriteValue(sb, entry.Value);
                }
                sb.Append('}');
                return;
            }

            if (value is IEnumerable seq && !(value is string))
            {
                sb.Append('[');
                bool first = true;
                foreach (object item in seq)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteValue(sb, item);
                }
                sb.Append(']');
                return;
            }

            if (value is char c) { WriteString(sb, c.ToString()); return; }
            if (value is Enum) { WriteString(sb, value.ToString()); return; }

            if (value is float f) { sb.Append(FormatNumber(f)); return; }
            if (value is double d) { sb.Append(FormatNumber(d)); return; }
            if (value is decimal m) { sb.Append(m.ToString(CultureInfo.InvariantCulture)); return; }

            if (value is byte || value is sbyte || value is short || value is ushort ||
                value is int || value is uint || value is long || value is ulong)
            {
                sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                return;
            }

            WriteString(sb, Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        private static string FormatNumber(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return "null";
            if (d == Math.Floor(d) && Math.Abs(d) < 1e15)
                return ((long)d).ToString(CultureInfo.InvariantCulture);
            return d.ToString("R", CultureInfo.InvariantCulture);
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            if (s == null) { sb.Append("null"); return; }
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 0x20)
                            sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(ch);
                        break;
                }
            }
            sb.Append('"');
        }

        private sealed class Parser
        {
            private readonly string _json;
            private int _index;

            public Parser(string json) { _json = json; _index = 0; }

            public int Position => _index;
            public bool AtEnd => _index >= _json.Length;

            public void SkipWhitespace()
            {
                while (_index < _json.Length)
                {
                    char c = _json[_index];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') { _index++; continue; }
                    // Tolerate a UTF-8 BOM that survived decoding.
                    if (c == '\uFEFF') { _index++; continue; }
                    break;
                }
            }

            public object ParseValue()
            {
                SkipWhitespace();
                if (AtEnd) throw new FormatException("Unexpected end of JSON.");

                char c = _json[_index];
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default: return ParseNumber();
                }
            }

            private void Expect(string literal)
            {
                if (_index + literal.Length > _json.Length ||
                    string.CompareOrdinal(_json, _index, literal, 0, literal.Length) != 0)
                    throw new FormatException($"Expected '{literal}' at position {_index}.");
                _index += literal.Length;
            }

            private Dictionary<string, object> ParseObject()
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                _index++; // {
                SkipWhitespace();
                if (!AtEnd && _json[_index] == '}') { _index++; return result; }

                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _json[_index] != '"')
                        throw new FormatException($"Expected object key at position {_index}.");
                    string key = ParseString();
                    SkipWhitespace();
                    if (AtEnd || _json[_index] != ':')
                        throw new FormatException($"Expected ':' at position {_index}.");
                    _index++;
                    result[key] = ParseValue();
                    SkipWhitespace();
                    if (AtEnd) throw new FormatException("Unterminated object.");
                    char c = _json[_index];
                    if (c == ',') { _index++; continue; }
                    if (c == '}') { _index++; return result; }
                    throw new FormatException($"Expected ',' or '}}' at position {_index}.");
                }
            }

            private List<object> ParseArray()
            {
                var result = new List<object>();
                _index++; // [
                SkipWhitespace();
                if (!AtEnd && _json[_index] == ']') { _index++; return result; }

                while (true)
                {
                    result.Add(ParseValue());
                    SkipWhitespace();
                    if (AtEnd) throw new FormatException("Unterminated array.");
                    char c = _json[_index];
                    if (c == ',') { _index++; continue; }
                    if (c == ']') { _index++; return result; }
                    throw new FormatException($"Expected ',' or ']' at position {_index}.");
                }
            }

            private string ParseString()
            {
                _index++; // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw new FormatException("Unterminated string.");
                    char c = _json[_index++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }

                    if (AtEnd) throw new FormatException("Unterminated escape sequence.");
                    char esc = _json[_index++];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_index + 4 > _json.Length) throw new FormatException("Truncated \\u escape.");
                            string hex = _json.Substring(_index, 4);
                            _index += 4;
                            sb.Append((char)Convert.ToInt32(hex, 16));
                            break;
                        default: throw new FormatException($"Invalid escape '\\{esc}'.");
                    }
                }
            }

            private double ParseNumber()
            {
                int start = _index;
                if (!AtEnd && (_json[_index] == '-' || _json[_index] == '+')) _index++;
                while (!AtEnd)
                {
                    char c = _json[_index];
                    if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-')
                    {
                        _index++;
                        continue;
                    }
                    break;
                }

                if (start == _index) throw new FormatException($"Unexpected character at position {_index}.");
                string text = _json.Substring(start, _index - start);
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                    throw new FormatException($"Invalid number '{text}'.");
                return value;
            }
        }
    }
}
