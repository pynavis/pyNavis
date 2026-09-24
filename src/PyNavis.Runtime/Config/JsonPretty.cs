using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PyNavis.Runtime.Config
{
    /// <summary>
    /// Minimal 2-space-indent JSON writer for the object graphs JavaScriptSerializer
    /// produces (Dictionary&lt;string, object&gt;, IList, string, bool, numbers, null).
    /// Exists because JavaScriptSerializer can only emit one-line JSON, and
    /// config.json is a file users read and edit by hand.
    /// </summary>
    internal static class JsonPretty
    {
        public static string Write(object node)
        {
            var sb = new StringBuilder();
            WriteNode(sb, node, 0);
            sb.AppendLine();
            return sb.ToString();
        }

        private static void WriteNode(StringBuilder sb, object node, int depth)
        {
            switch (node)
            {
                case null:
                    sb.Append("null");
                    break;
                case string s:
                    WriteString(sb, s);
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case IDictionary<string, object> map:
                    WriteMap(sb, map, depth);
                    break;
                case IEnumerable list:
                    WriteList(sb, list, depth);
                    break;
                default: // int, long, double, decimal from the deserializer
                    sb.Append(System.Convert.ToString(node, CultureInfo.InvariantCulture));
                    break;
            }
        }

        private static void WriteMap(StringBuilder sb, IDictionary<string, object> map, int depth)
        {
            if (map.Count == 0)
            {
                sb.Append("{}");
                return;
            }
            sb.Append("{\n");
            var i = 0;
            foreach (var pair in map)
            {
                Indent(sb, depth + 1);
                WriteString(sb, pair.Key);
                sb.Append(": ");
                WriteNode(sb, pair.Value, depth + 1);
                sb.Append(++i < map.Count ? ",\n" : "\n");
            }
            Indent(sb, depth);
            sb.Append('}');
        }

        private static void WriteList(StringBuilder sb, IEnumerable list, int depth)
        {
            var items = new List<object>();
            foreach (var item in list) items.Add(item);
            if (items.Count == 0)
            {
                sb.Append("[]");
                return;
            }
            sb.Append("[\n");
            for (var i = 0; i < items.Count; i++)
            {
                Indent(sb, depth + 1);
                WriteNode(sb, items[i], depth + 1);
                sb.Append(i + 1 < items.Count ? ",\n" : "\n");
            }
            Indent(sb, depth);
            sb.Append(']');
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        private static void Indent(StringBuilder sb, int depth) => sb.Append(' ', depth * 2);
    }
}
