using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PyNavis.Runtime.Ai
{
    public sealed class SseEvent
    {
        public string Event;
        public string Data;
    }

    /// <summary>
    /// Server-Sent Events framing: "field: value" lines, a blank line ends an event,
    /// several data lines join with newlines, a leading colon is a comment.
    /// </summary>
    public static class SseReader
    {
        /// <summary>The pure half, over lines already split.</summary>
        public static IEnumerable<SseEvent> Parse(IEnumerable<string> lines)
        {
            string name = null;
            var data = new StringBuilder();
            var hasData = false;

            foreach (var raw in lines)
            {
                var line = raw ?? "";
                if (line.Length == 0)
                {
                    if (hasData || name != null)
                        yield return new SseEvent { Event = name, Data = hasData ? data.ToString() : null };
                    name = null;
                    data.Clear();
                    hasData = false;
                    continue;
                }
                if (line[0] == ':') continue;

                var colon = line.IndexOf(':');
                var field = colon < 0 ? line : line.Substring(0, colon);
                var value = colon < 0 ? "" : line.Substring(colon + 1);
                if (value.StartsWith(" ")) value = value.Substring(1);

                if (field == "event") name = value;
                else if (field == "data")
                {
                    if (hasData) data.Append('\n');
                    data.Append(value);
                    hasData = true;
                }
                // id, retry and anything else: ignored
            }
            if (hasData || name != null)
                yield return new SseEvent { Event = name, Data = hasData ? data.ToString() : null };
        }

        /// <summary>Reads events as lines arrive. <paramref name="onEvent"/> returns
        /// false to stop reading.</summary>
        public static async Task ReadAsync(TextReader reader, Func<SseEvent, bool> onEvent, CancellationToken token)
        {
            string name = null;
            var data = new StringBuilder();
            var hasData = false;

            bool Flush()
            {
                if (!hasData && name == null) return true;
                var evt = new SseEvent { Event = name, Data = hasData ? data.ToString() : null };
                name = null;
                data.Clear();
                hasData = false;
                return onEvent(evt);
            }

            while (true)
            {
                token.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line == null) break;
                if (line.Length == 0)
                {
                    if (!Flush()) return;
                    continue;
                }
                if (line[0] == ':') continue;

                var colon = line.IndexOf(':');
                var field = colon < 0 ? line : line.Substring(0, colon);
                var value = colon < 0 ? "" : line.Substring(colon + 1);
                if (value.StartsWith(" ")) value = value.Substring(1);

                if (field == "event") name = value;
                else if (field == "data")
                {
                    if (hasData) data.Append('\n');
                    data.Append(value);
                    hasData = true;
                }
            }
            Flush();
        }
    }
}
