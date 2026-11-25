using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Codely.UnityMcp
{
    /// <summary>
    /// Captures console output so agents can read diagnostics without opening the
    /// Console window. Entries are collected on Unity's log callback (which fires on
    /// arbitrary threads) behind a lock, and served in a stable snapshot.
    /// </summary>
    public static class ConsoleBuffer
    {
        private const int Capacity = 2000;
        private const string PrefGrouping = "Codely.UnityMcp.ConsoleGrouping";

        private sealed class Entry
        {
            public DateTime TimestampUtc;
            public LogType Type;
            public string Message;
            public string StackTrace;
        }

        private static readonly object Gate = new object();
        private static readonly List<Entry> Entries = new List<Entry>(Capacity);
        private static long _version;
        private static bool _initialized;

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            Application.logMessageReceivedThreaded -= OnLog;
            Application.logMessageReceivedThreaded += OnLog;
        }

        private static void OnLog(string message, string stackTrace, LogType type)
        {
            lock (Gate)
            {
                Entries.Add(new Entry
                {
                    TimestampUtc = DateTime.UtcNow,
                    Type = type,
                    Message = message ?? string.Empty,
                    StackTrace = stackTrace ?? string.Empty
                });
                if (Entries.Count > Capacity)
                    Entries.RemoveRange(0, Entries.Count - Capacity);
                _version++;
            }
        }

        public static long Version { get { lock (Gate) return _version; } }

        public static Dictionary<string, object> Read(IDictionary<string, object> p)
        {
            Initialize();

            string[] typeFilter = JsonUtil.StrArray(p, "types");
            if (typeFilter.Length == 0) typeFilter = new[] { "error", "warning", "log" };

            var allowed = new HashSet<LogType>();
            foreach (var name in typeFilter)
            {
                switch ((name ?? string.Empty).Trim().ToLowerInvariant())
                {
                    case "error": allowed.Add(LogType.Error); allowed.Add(LogType.Exception); break;
                    case "warning": allowed.Add(LogType.Warning); allowed.Add(LogType.Assert); break;
                    case "log": allowed.Add(LogType.Log); break;
                    case "assert": allowed.Add(LogType.Assert); break;
                    case "exception": allowed.Add(LogType.Exception); break;
                    case "all":
                        allowed.Add(LogType.Error); allowed.Add(LogType.Warning); allowed.Add(LogType.Log);
                        allowed.Add(LogType.Assert); allowed.Add(LogType.Exception);
                        break;
                }
            }
            if (allowed.Count == 0)
            {
                allowed.Add(LogType.Error); allowed.Add(LogType.Warning); allowed.Add(LogType.Log);
            }

            int count = Mathf.Clamp(JsonUtil.Int(p, "count", 50), 1, 1000);
            string filterText = JsonUtil.Str(p, "filterText", null, "filter");
            string format = (JsonUtil.Str(p, "format", "plain") ?? "plain").ToLowerInvariant();
            bool includeStack = JsonUtil.Bool(p, "includeStacktrace", format == "detailed");
            bool collapse = JsonUtil.Bool(p, "collapse", true);
            DateTime? since = ParseSince(JsonUtil.Str(p, "sinceTimestamp"));

            List<Entry> snapshot;
            lock (Gate)
            {
                snapshot = new List<Entry>(Entries);
            }

            var matched = new List<Entry>();
            foreach (var entry in snapshot)
            {
                if (!allowed.Contains(entry.Type)) continue;
                if (since.HasValue && entry.TimestampUtc <= since.Value) continue;
                if (!string.IsNullOrEmpty(filterText) &&
                    entry.Message.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                matched.Add(entry);
            }

            if (collapse)
                matched = Collapse(matched);

            int total = matched.Count;
            bool truncated = total > count;
            if (truncated)
                matched = matched.GetRange(total - count, count);

            var items = new List<object>(matched.Count);
            foreach (var entry in matched)
            {
                if (format == "json")
                {
                    items.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["type"] = entry.Type.ToString().ToLowerInvariant(),
                        ["message"] = entry.Message,
                        ["timestamp"] = entry.TimestampUtc.ToString("O", CultureInfo.InvariantCulture),
                        ["stackTrace"] = includeStack ? entry.StackTrace : null
                    });
                }
                else if (format == "detailed")
                {
                    var first = FirstStackLine(entry.StackTrace);
                    items.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["type"] = entry.Type.ToString().ToLowerInvariant(),
                        ["message"] = entry.Message,
                        ["file"] = first.File,
                        ["line"] = first.Line,
                        ["stackTrace"] = includeStack ? entry.StackTrace : null,
                        ["timestamp"] = entry.TimestampUtc.ToString("O", CultureInfo.InvariantCulture)
                    });
                }
                else
                {
                    items.Add(entry.Message);
                }
            }

            int errors = 0, warnings = 0, logs = 0;
            foreach (var entry in snapshot)
            {
                if (entry.Type == LogType.Error || entry.Type == LogType.Exception) errors++;
                else if (entry.Type == LogType.Warning || entry.Type == LogType.Assert) warnings++;
                else logs++;
            }

            var diagnosisInput = new List<string>(matched.Count);
            foreach (var entry in matched) diagnosisInput.Add(entry.Message);

            var result = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["format"] = format,
                ["count"] = items.Count,
                ["total_matched"] = total,
                ["truncated"] = truncated,
                ["collapsed"] = collapse,
                ["captured_total"] = snapshot.Count,
                ["error_count"] = errors,
                ["warning_count"] = warnings,
                ["log_count"] = logs,
                ["diagnosis"] = AiContract.Diagnose(diagnosisInput),
                ["entries"] = items
            };

            if (format == "plain")
                result["text"] = string.Join(Environment.NewLine, items.ConvertAll(x => Convert.ToString(x, CultureInfo.InvariantCulture)));

            if (truncated)
                result["hint"] = "Only the newest entries are returned. Increase count, or narrow types/filterText, to see more.";
            if (errors == 0 && warnings == 0)
                result["hint"] = "No errors or warnings are currently captured. If the project was just opened, recent logs may predate the bridge.";

            return result;
        }

        public static Dictionary<string, object> Clear(IDictionary<string, object> p)
        {
            Initialize();
            lock (Gate) { Entries.Clear(); _version++; }
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["cleared"] = true,
                ["note"] = "scope 'errors_only' is accepted for compatibility, but Unity has no API to clear only errors; everything is cleared."
            };
        }

        public static Dictionary<string, object> SetGrouping(IDictionary<string, object> p)
        {
            bool collapse = JsonUtil.Bool(p, "collapse", true);
            EditorPrefs.SetBool(PrefGrouping, collapse);
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["collapse"] = collapse,
                ["note"] = "Stored as a preference. Use the 'collapse' parameter on get for per-call grouping."
            };
        }

        private static List<Entry> Collapse(List<Entry> entries)
        {
            var result = new List<Entry>(entries.Count);
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                string key = ((int)entry.Type).ToString(CultureInfo.InvariantCulture) + "|" + entry.Message;
                if (index.TryGetValue(key, out int existing))
                {
                    var target = result[existing];
                    target.Message = AddCountSuffix(StripCountSuffix(target.Message));
                }
                else
                {
                    index[key] = result.Count;
                    result.Add(new Entry
                    {
                        TimestampUtc = entry.TimestampUtc,
                        Type = entry.Type,
                        Message = entry.Message,
                        StackTrace = entry.StackTrace
                    });
                }
            }
            return result;
        }

        private static string AddCountSuffix(string message)
        {
            int marker = message.LastIndexOf(" [x", StringComparison.Ordinal);
            if (marker >= 0 && message.EndsWith("]", StringComparison.Ordinal))
            {
                string digits = message.Substring(marker + 3, message.Length - marker - 4);
                if (int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                    return message.Substring(0, marker) + " [x" + (n + 1).ToString(CultureInfo.InvariantCulture) + "]";
            }
            return message + " [x2]";
        }

        private static string StripCountSuffix(string message)
        {
            int marker = message.LastIndexOf(" [x", StringComparison.Ordinal);
            return marker >= 0 && message.EndsWith("]", StringComparison.Ordinal)
                ? message.Substring(0, marker)
                : message;
        }

        private static (string File, int Line) FirstStackLine(string stackTrace)
        {
            if (string.IsNullOrEmpty(stackTrace)) return (null, 0);
            string first = stackTrace.Split('\n')[0].Trim();
            int inIndex = first.IndexOf(" (at ", StringComparison.Ordinal);
            if (inIndex < 0) return (first, 0);
            string location = first.Substring(inIndex + 5).TrimEnd(')');
            int colon = location.LastIndexOf(':');
            if (colon < 0) return (location, 0);
            return int.TryParse(location.Substring(colon + 1), out int line)
                ? (location.Substring(0, colon), line)
                : (location, 0);
        }

        private static DateTime? ParseSince(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            return DateTime.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed)
                ? parsed
                : (DateTime?)null;
        }
    }
}
