using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Web.Script.Serialization;

namespace PyNavis.Runtime.Telemetry
{
    /// <summary>
    /// One thing a tool did, as the firm's telemetry stores it: one JSON object on one
    /// line, its keys always in the same order. A record holds no model content: the
    /// model's file name without its folder, the tool, how long it took and whether it
    /// worked. A script may add its own values under "results".
    /// </summary>
    public sealed class TelemetryRecord
    {
        /// <summary>The shape of a line; bumped only when a field changes meaning.</summary>
        public const int SchemaVersion = 1;

        /// <summary>The longest error line kept; a traceback's first line rarely needs more.</summary>
        public const int MaxErrorLength = 500;

        public const string TimeFormat = "yyyy-MM-ddTHH:mm:ss.fffK";

        /// <summary>The largest line a record makes, in UTF-8 bytes. Only "results" can
        /// grow past it (every other field is short or capped), and then they are left out.</summary>
        public const int MaxLineBytes = 64 * 1024;

        /// <summary>One per Navisworks session (this process), so a reader can group the
        /// clicks of one sitting.</summary>
        public static string CurrentSession { get; } = Guid.NewGuid().ToString("D");

        public int Schema { get; set; } = SchemaVersion;
        public DateTimeOffset Time { get; set; }
        public string Session { get; set; }
        /// <summary>The Windows user name.</summary>
        public string User { get; set; }
        /// <summary>Environment.MachineName.</summary>
        public string Machine { get; set; }
        /// <summary>The Navisworks year, "2026".</summary>
        public string Navis { get; set; }
        /// <summary>The pyNavis version, "1.3.0".</summary>
        public string PyNavis { get; set; }
        /// <summary>The open model. A full path is fine here: only the file name is written.</summary>
        public string Doc { get; set; }
        /// <summary>"click", "config", "hook", "run", or "test" for Settings' Send test record.</summary>
        public string Kind { get; set; }
        /// <summary>The bundle key, the extension-relative path the runtime uses elsewhere.</summary>
        public string Key { get; set; }
        public string Title { get; set; }
        /// <summary>The extension's name.</summary>
        public string Extension { get; set; }
        /// <summary>"ironpython" or "cpython".</summary>
        public string Engine { get; set; }
        public long Ms { get; set; }
        /// <summary>"ok" or "error".</summary>
        public string Result { get; set; } = "ok";
        /// <summary>The error text; only its ErrorSummary line is written, and only when
        /// Result is not "ok".</summary>
        public string Error { get; set; }
        /// <summary>Values the script reports for itself; omitted from the line when empty.</summary>
        public Dictionary<string, object> Results { get; set; }

        /// <summary>
        /// The record as one line of JSON (no line end). Results that are not plain JSON
        /// values are written as their text, so they never make this throw.
        ///
        /// The cap: when "results" would take the line past MaxLineBytes, or cannot be
        /// written at all (past the serializer's own limit), the line is written again
        /// without them and ends with "results_dropped": true instead, so a reader can tell
        /// a run that noted nothing from one whose notes were too big. One huge note would
        /// otherwise make a record a server refuses, or fill the spool in place of hundreds
        /// of others.
        /// </summary>
        public string ToJsonLine()
        {
            var result = string.IsNullOrWhiteSpace(Result) ? "ok" : Result.Trim();
            var data = new Dictionary<string, object>
            {
                ["schema"] = Schema,
                ["time"] = Time.ToString(TimeFormat, CultureInfo.InvariantCulture),
                ["session"] = Session ?? "",
                ["user"] = User ?? "",
                ["machine"] = Machine ?? "",
                ["navis"] = Navis ?? "",
                ["pynavis"] = PyNavis ?? "",
                ["doc"] = FileNameOnly(Doc),
                ["kind"] = Kind ?? "",
                ["key"] = Key ?? "",
                ["title"] = Title ?? "",
                ["extension"] = Extension ?? "",
                ["engine"] = Engine ?? "",
                ["ms"] = Ms,
                ["result"] = result,
                ["error"] = result == "ok" ? "" : ErrorSummary(Error),
            };
            var serializer = new JavaScriptSerializer();
            if (Results == null || Results.Count == 0) return serializer.Serialize(data);

            data["results"] = Plain(Results, 0);
            string line = null;
            try { line = serializer.Serialize(data); }
            catch (InvalidOperationException) { }           // longer than the serializer's MaxJsonLength
            catch (ArgumentException) { }                   // nested deeper than its RecursionLimit
            if (line != null && Encoding.UTF8.GetByteCount(line) <= MaxLineBytes) return line;

            data.Remove("results");
            data["results_dropped"] = true;
            return serializer.Serialize(data);
        }

        /// <summary>Marks the Python traceback that both engines hand back for a script
        /// that raised.</summary>
        public const string TracebackHeader = "Traceback (most recent call last)";

        /// <summary>
        /// The one line of an error worth keeping. A Python traceback's first line only
        /// says a traceback follows, and the lines after it carry the script's path, so a
        /// traceback gives its LAST line that is not blank: the exception and its message.
        /// Any other text gives its first line that is not blank. Trimmed and cut at
        /// MaxErrorLength; "" for null or blank text.
        /// </summary>
        public static string ErrorSummary(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text.IndexOf(TracebackHeader, StringComparison.Ordinal) < 0) return FirstLine(text);
            var lines = text.Split('\r', '\n');
            for (var i = lines.Length - 1; i >= 0; i--)
            {
                var line = lines[i].Trim();
                if (line.Length > 0) return Cap(line);
            }
            return "";
        }

        /// <summary>The first line of <paramref name="text"/> that is not blank, trimmed
        /// and cut at MaxErrorLength; "" for null or blank text.</summary>
        public static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            foreach (var raw in text.Split('\r', '\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                return Cap(line);
            }
            return "";
        }

        private static string Cap(string line) =>
            line.Length > MaxErrorLength ? line.Substring(0, MaxErrorLength) : line;

        /// <summary>A copy of <paramref name="value"/> as plain JSON values, taken when a
        /// script notes it, so the script changing its list afterwards changes nothing and
        /// the worker thread never touches a script's own objects. Never throws.</summary>
        internal static object Snapshot(object value)
        {
            try { return Plain(value, 0); }
            catch (Exception) { return null; }
        }

        /// <summary>What follows the last slash or backslash. Path.GetFileName is not used
        /// because it throws on characters a document title may carry.</summary>
        internal static string FileNameOnly(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            var trimmed = path.Trim();
            var cut = trimmed.LastIndexOfAny(new[] { '\\', '/' });
            return cut < 0 ? trimmed : trimmed.Substring(cut + 1);
        }

        private const int MaxDepth = 8;

        /// <summary>A value JavaScriptSerializer writes as plain JSON: maps keyed by text,
        /// lists, text, numbers, booleans and null. Dates become ISO text (not the
        /// serializer's own date form), numbers JSON cannot hold become null, and anything
        /// else becomes its text.</summary>
        private static object Plain(object value, int depth)
        {
            switch (value)
            {
                case null:
                    return null;
                case string _:
                case bool _:
                case int _:
                case long _:
                case short _:
                case byte _:
                case sbyte _:
                case ushort _:
                case uint _:
                case ulong _:
                case decimal _:
                    return value;
                case double d:
                    return double.IsNaN(d) || double.IsInfinity(d) ? (object)null : d;
                case float f:
                    return float.IsNaN(f) || float.IsInfinity(f) ? (object)null : f;
                case DateTime date:
                    return date.ToString(TimeFormat, CultureInfo.InvariantCulture);
                case DateTimeOffset whenOffset:
                    return whenOffset.ToString(TimeFormat, CultureInfo.InvariantCulture);
                case Enum _:
                    return value.ToString();
            }

            var text = Convert.ToString(value, CultureInfo.InvariantCulture);
            // IronPython hands over ints past Int32 as BigInteger.
            if (value.GetType().FullName == "System.Numerics.BigInteger")
                return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var big)
                    ? (object)big : text;
            if (depth >= MaxDepth) return text;

            if (value is IDictionary map)
            {
                var plain = new Dictionary<string, object>();
                foreach (DictionaryEntry entry in map)
                    plain[Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? ""] = Plain(entry.Value, depth + 1);
                return plain;
            }
            if (value is IEnumerable list)
            {
                var plain = new List<object>();
                foreach (var item in list) plain.Add(Plain(item, depth + 1));
                return plain;
            }
            return text;
        }
    }
}
