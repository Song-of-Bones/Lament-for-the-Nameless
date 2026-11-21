using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Codely.UnityMcp
{
    /// <summary>
    /// Typed accessors over MiniJson output. Handlers receive loosely typed params, so
    /// every read goes through here to keep coercion rules (aliases, casing, defaults)
    /// in one place instead of being re-implemented by each tool.
    /// </summary>
    public static class JsonUtil
    {
        public static IDictionary<string, object> AsObject(object value)
            => value as IDictionary<string, object>;

        public static IDictionary<string, object> Params(object raw)
            => raw as IDictionary<string, object> ?? new Dictionary<string, object>(StringComparer.Ordinal);

        public static bool Has(IDictionary<string, object> obj, params string[] keys)
        {
            if (obj == null) return false;
            foreach (var key in keys)
                if (!string.IsNullOrEmpty(key) && obj.ContainsKey(key) && obj[key] != null)
                    return true;
            return false;
        }

        public static object Raw(IDictionary<string, object> obj, params string[] keys)
        {
            if (obj == null) return null;
            foreach (var key in keys)
            {
                if (string.IsNullOrEmpty(key)) continue;
                if (obj.TryGetValue(key, out object value) && value != null) return value;
            }
            return null;
        }

        public static string Str(IDictionary<string, object> obj, string key, string fallback = null, params string[] aliases)
        {
            object value = Raw(obj, Combine(key, aliases));
            if (value == null) return fallback;
            if (value is string s) return s;
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public static int Int(IDictionary<string, object> obj, string key, int fallback = 0, params string[] aliases)
        {
            object value = Raw(obj, Combine(key, aliases));
            if (value == null) return fallback;
            if (value is double d) return (int)Math.Round(d);
            if (value is bool b) return b ? 1 : 0;
            return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;
        }

        public static float Float(IDictionary<string, object> obj, string key, float fallback = 0f, params string[] aliases)
        {
            object value = Raw(obj, Combine(key, aliases));
            if (value == null) return fallback;
            if (value is double d) return (float)d;
            if (value is bool b) return b ? 1f : 0f;
            return float.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float parsed) ? parsed : fallback;
        }

        public static bool Bool(IDictionary<string, object> obj, string key, bool fallback = false, params string[] aliases)
        {
            object value = Raw(obj, Combine(key, aliases));
            if (value == null) return fallback;
            if (value is bool b) return b;
            if (value is double d) return Math.Abs(d) > double.Epsilon;
            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (bool.TryParse(text, out bool parsed)) return parsed;
            return text == "1" || string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase);
        }

        public static List<object> List(IDictionary<string, object> obj, string key, params string[] aliases)
            => Raw(obj, Combine(key, aliases)) as List<object> ?? new List<object>();

        public static IDictionary<string, object> Obj(IDictionary<string, object> obj, string key, params string[] aliases)
            => Raw(obj, Combine(key, aliases)) as IDictionary<string, object>;

        public static string[] StrArray(IDictionary<string, object> obj, string key, params string[] aliases)
        {
            var list = List(obj, key, aliases);
            if (list.Count == 0)
            {
                string single = Str(obj, key);
                return string.IsNullOrEmpty(single) ? Array.Empty<string>() : new[] { single };
            }

            var result = new List<string>(list.Count);
            foreach (var item in list)
            {
                string text = item as string ?? Convert.ToString(item, CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(text)) result.Add(text);
            }
            return result.ToArray();
        }

        public static bool TryVec3(IDictionary<string, object> obj, string key, out Vector3 value, params string[] aliases)
        {
            value = Vector3.zero;
            object raw = Raw(obj, Combine(key, aliases));
            if (raw == null) return false;

            if (raw is List<object> list && list.Count >= 3)
            {
                value = new Vector3(ToFloat(list[0]), ToFloat(list[1]), ToFloat(list[2]));
                return true;
            }

            if (raw is IDictionary<string, object> dict)
            {
                value = new Vector3(Float(dict, "x"), Float(dict, "y"), Float(dict, "z"));
                return true;
            }

            string text = raw as string;
            if (!string.IsNullOrEmpty(text))
            {
                string[] parts = text.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    value = new Vector3(ToFloat(parts[0]), ToFloat(parts[1]), ToFloat(parts[2]));
                    return true;
                }
            }
            return false;
        }

        public static float ToFloat(object value)
        {
            if (value is double d) return (float)d;
            if (value is float f) return f;
            if (value is int i) return i;
            return float.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float parsed) ? parsed : 0f;
        }

        public static Dictionary<string, object> Dict(params object[] keyValues)
        {
            var dict = new Dictionary<string, object>(StringComparer.Ordinal);
            for (int i = 0; i + 1 < keyValues.Length; i += 2)
                dict[Convert.ToString(keyValues[i], CultureInfo.InvariantCulture)] = keyValues[i + 1];
            return dict;
        }

        public static List<object> NewList() => new List<object>();

        private static string[] Combine(string key, string[] aliases)
        {
            if (aliases == null || aliases.Length == 0) return new[] { key };
            var all = new string[aliases.Length + 1];
            all[0] = key;
            Array.Copy(aliases, 0, all, 1, aliases.Length);
            return all;
        }
    }
}
