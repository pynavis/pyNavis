using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace PyNavis.Runtime.Telemetry
{
    /// <summary>
    /// Records a sink could not take yet, kept on disk (one JSON line each) until a later
    /// flush sends them, across Navisworks sessions. One spool file per sink, so a sink
    /// that works is never sent the same record twice because another one failed.
    ///
    /// The cap: when an append takes the file past MaxBytes, the oldest lines are
    /// dropped until what is left fits in three quarters of MaxBytes. Trimming to below
    /// the cap means a long outage rewrites the file now and then, not on every flush.
    ///
    /// Every open is exclusive (two Navisworks windows share the file, and TakeAll must
    /// hand each line to only one of them), retried briefly when the other one has it.
    /// </summary>
    public sealed class TelemetrySpool
    {
        public const long DefaultMaxBytes = 5L * 1024 * 1024;

        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        public TelemetrySpool(string path, long maxBytes = DefaultMaxBytes)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A spool needs a file path.", nameof(path));
            Path = path;
            MaxBytes = maxBytes > 0 ? maxBytes : DefaultMaxBytes;
        }

        public string Path { get; }
        public long MaxBytes { get; }

        /// <summary>Adds lines at the end; returns how many lines the cap dropped (oldest
        /// first, which can include some of these when they alone exceed it). Throws on IO
        /// failure.</summary>
        public int Append(IEnumerable<string> lines)
        {
            var list = (lines ?? Enumerable.Empty<string>()).Where(IsRecordLine).ToList();
            if (list.Count == 0) return 0;

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path)));
            using (var stream = Open(FileMode.OpenOrCreate, FileAccess.ReadWrite))
            {
                stream.Seek(0, SeekOrigin.End);
                var bytes = Utf8.GetBytes(Join(list));
                stream.Write(bytes, 0, bytes.Length);
                return stream.Length > MaxBytes ? Trim(stream) : 0;
            }
        }

        /// <summary>Every complete line, oldest first, leaving the file empty. A missing
        /// file is no lines. Throws on IO failure, and then the lines stay in the file.</summary>
        public List<string> TakeAll()
        {
            if (!File.Exists(Path)) return new List<string>();
            using (var stream = Open(FileMode.Open, FileAccess.ReadWrite))
            {
                if (stream.Length == 0) return new List<string>();
                var lines = ReadLines(stream);
                stream.SetLength(0);
                return lines;
            }
        }

        /// <summary>How many complete lines wait in the file; 0 when it cannot be read.</summary>
        public int Count()
        {
            try
            {
                if (!File.Exists(Path)) return 0;
                using (var stream = Open(FileMode.Open, FileAccess.Read))
                    return stream.Length == 0 ? 0 : ReadLines(stream).Count;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private int Trim(FileStream stream)
        {
            stream.Position = 0;
            var all = ReadLines(stream);
            var budget = MaxBytes * 3 / 4;
            long size = 0;
            var first = all.Count;
            while (first > 0)
            {
                var bytes = Utf8.GetByteCount(all[first - 1]) + 1;
                if (size + bytes > budget) break;
                size += bytes;
                first--;
            }
            var keep = all.GetRange(first, all.Count - first);
            stream.SetLength(0);
            var kept = Utf8.GetBytes(Join(keep));
            stream.Write(kept, 0, kept.Length);
            return all.Count - keep.Count;
        }

        private static List<string> ReadLines(Stream stream)
        {
            stream.Position = 0;
            string text;
            using (var reader = new StreamReader(stream, Utf8, true, 4096, leaveOpen: true))
                text = reader.ReadToEnd();
            return text.Split('\n').Select(l => l.TrimEnd('\r')).Where(IsRecordLine).ToList();
        }

        /// <summary>A whole JSON object on one line; anything else (a blank line, the torn
        /// end of a write a crash cut short) is not worth keeping.</summary>
        private static bool IsRecordLine(string line) =>
            !string.IsNullOrEmpty(line) && line[0] == '{' && line[line.Length - 1] == '}'
            && line.IndexOf('\n') < 0 && line.IndexOf('\r') < 0;

        private static string Join(IEnumerable<string> lines)
        {
            var sb = new StringBuilder();
            foreach (var line in lines) sb.Append(line).Append('\n');
            return sb.ToString();
        }

        private FileStream Open(FileMode mode, FileAccess access)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return new FileStream(Path, mode, access, FileShare.None);
                }
                catch (IOException ex) when (attempt < 10
                    && !(ex is FileNotFoundException) && !(ex is DirectoryNotFoundException))
                {
                    Thread.Sleep(20);                       // the other Navisworks has it
                }
            }
        }
    }
}
