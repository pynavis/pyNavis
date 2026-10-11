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
    /// Read, then discard: a flush reads the lines with PeekAll, which leaves them in the
    /// file, and takes out only the ones a sink took with Discard once the send has
    /// worked. A Navisworks that exits in the middle of a slow send so leaves its backlog
    /// on disk for next time. Two Navisworks windows of one user share the file, so
    /// Discard removes the lines it is given wherever they are, never simply the first N:
    /// lines the other window added meanwhile stay. Both windows can read the same lines
    /// before either discards them, so a record can now and then reach a sink twice (its
    /// session and time tell the copies apart); it is never lost.
    ///
    /// The cap: when an append takes the file past MaxBytes, the oldest lines are
    /// dropped until what is left fits in three quarters of MaxBytes. Trimming to below
    /// the cap means a long outage rewrites the file now and then, not on every flush.
    ///
    /// Every open is exclusive, retried briefly when the other window has the file.
    /// Count is kept in memory as this spool changes the file, so the Settings window's
    /// status line never waits on the disk.
    /// </summary>
    public sealed class TelemetrySpool
    {
        public const long DefaultMaxBytes = 5L * 1024 * 1024;

        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        private readonly object _gate = new object();
        private int _count = -1;                            // lines in the file as last seen; -1: not looked yet

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

            lock (_gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path)));
                using (var stream = Open(FileMode.OpenOrCreate, FileAccess.ReadWrite))
                {
                    var before = Known() ?? (stream.Length == 0 ? 0 : ReadLines(stream).Count);
                    var text = Join(list);
                    if (stream.Length > 0)
                    {
                        // The torn end of a write a crash cut short gets a line end of its own,
                        // so it never joins the first new line into one that looks whole.
                        stream.Seek(-1, SeekOrigin.End);
                        if (stream.ReadByte() != '\n') text = "\n" + text;
                    }
                    stream.Seek(0, SeekOrigin.End);
                    var bytes = Utf8.GetBytes(text);
                    stream.Write(bytes, 0, bytes.Length);
                    if (stream.Length <= MaxBytes)
                    {
                        SetCount(before + list.Count);
                        return 0;
                    }
                    var (dropped, kept) = Trim(stream);
                    SetCount(kept);
                    return dropped;
                }
            }
        }

        /// <summary>Every complete line, oldest first, leaving the file as it is: Discard
        /// takes out the ones that were sent. A missing file is no lines. Throws on IO
        /// failure.</summary>
        public List<string> PeekAll()
        {
            lock (_gate)
            {
                if (!File.Exists(Path))
                {
                    SetCount(0);
                    return new List<string>();
                }
                using (var stream = Open(FileMode.Open, FileAccess.Read))
                {
                    var lines = stream.Length == 0 ? new List<string>() : ReadLines(stream);
                    SetCount(lines.Count);
                    return lines;
                }
            }
        }

        /// <summary>Takes <paramref name="lines"/> out of the file, one occurrence of each,
        /// oldest first, and keeps every other line in order, including any added since
        /// they were read. A line that is no longer there (the cap trimmed it, or the other
        /// window sent it) is skipped. Returns how many lines it took out. Throws on IO
        /// failure, and then the file is as it was.</summary>
        public int Discard(IEnumerable<string> lines)
        {
            var wanted = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var line in (lines ?? Enumerable.Empty<string>()).Where(IsRecordLine))
                wanted[line] = wanted.TryGetValue(line, out var n) ? n + 1 : 1;
            if (wanted.Count == 0) return 0;

            lock (_gate)
            {
                if (!File.Exists(Path))
                {
                    SetCount(0);
                    return 0;
                }
                using (var stream = Open(FileMode.Open, FileAccess.ReadWrite))
                {
                    var all = stream.Length == 0 ? new List<string>() : ReadLines(stream);
                    var keep = new List<string>(all.Count);
                    foreach (var line in all)
                    {
                        if (wanted.TryGetValue(line, out var n) && n > 0) wanted[line] = n - 1;
                        else keep.Add(line);
                    }
                    var removed = all.Count - keep.Count;
                    if (removed > 0) Rewrite(stream, keep);
                    SetCount(keep.Count);
                    return removed;
                }
            }
        }

        /// <summary>How many complete lines wait in the file, as this spool last saw it
        /// (reading, appending or discarding). Only the first call before any of those reads
        /// the file; 0 when it cannot be read. Another Navisworks window's changes show at
        /// this spool's next read.</summary>
        public int Count()
        {
            var known = Known();
            if (known != null) return known.Value;
            lock (_gate)
            {
                known = Known();
                if (known != null) return known.Value;
                try
                {
                    if (!File.Exists(Path))
                    {
                        SetCount(0);
                        return 0;
                    }
                    using (var stream = Open(FileMode.Open, FileAccess.Read))
                    {
                        var count = stream.Length == 0 ? 0 : ReadLines(stream).Count;
                        SetCount(count);
                        return count;
                    }
                }
                catch (Exception)
                {
                    return 0;                               // looked for again next time
                }
            }
        }

        /// <summary>
        /// Opens <paramref name="path"/>, retrying for about 200 ms while another process
        /// has it open in a way that conflicts (the other Navisworks window), then throws
        /// the IOException. A missing file or folder throws at once.
        /// </summary>
        internal static FileStream OpenWithRetry(string path, FileMode mode, FileAccess access, FileShare share)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return new FileStream(path, mode, access, share);
                }
                catch (IOException ex) when (attempt < 10
                    && !(ex is FileNotFoundException) && !(ex is DirectoryNotFoundException))
                {
                    Thread.Sleep(20);                       // the other Navisworks has it
                }
            }
        }

        private int? Known()
        {
            var count = Volatile.Read(ref _count);
            return count >= 0 ? count : (int?)null;
        }

        private void SetCount(int count) => Volatile.Write(ref _count, Math.Max(0, count));

        private (int Dropped, int Kept) Trim(FileStream stream)
        {
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
            Rewrite(stream, keep);
            return (all.Count - keep.Count, keep.Count);
        }

        private static void Rewrite(FileStream stream, List<string> lines)
        {
            stream.SetLength(0);
            stream.Position = 0;
            var bytes = Utf8.GetBytes(Join(lines));
            stream.Write(bytes, 0, bytes.Length);
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

        private FileStream Open(FileMode mode, FileAccess access) => OpenWithRetry(Path, mode, access, FileShare.None);
    }
}
