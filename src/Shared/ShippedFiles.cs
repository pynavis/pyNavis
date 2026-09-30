using System;
using System.Collections.Generic;
using System.Linq;

// One source, compiled into two assemblies: the runtime (the startup warning) and
// the standalone command line the installer runs before it replaces
// pyNavis.extension. The CLI stays free of any runtime reference, so each gets its
// own namespace instead of sharing a DLL.
#if PYNAVIS_CLI
namespace PyNavis.Cli.Upgrade
#else
namespace PyNavis.Runtime.Upgrade
#endif
{
    /// <summary>
    /// What the installer shipped in pyNavis.extension, and what someone added or
    /// changed there since. Every release lists its files with their SHA-256 (a
    /// ".pynavis-manifest" inside the extension, and one per past release inside
    /// the installer); a file that matches no release is someone's work.
    ///
    /// The plan says what happens to that work on an update: everything is backed
    /// up; added files that stand on their own (a new button, a new panel, a module
    /// in lib) also move to "My pyNavis.extension", where they keep working because
    /// tabs and panels merge by name; added files inside a shipped button or stack
    /// and edited shipped files can only be backed up, since the new version
    /// replaces what they sit in. Pure: paths and hashes in, lists out.
    /// </summary>
    public static class ShippedFiles
    {
        /// <summary>The manifest's name inside the extension it describes.</summary>
        public const string ManifestName = ".pynavis-manifest";

        // Folders that are one ribbon item (or a group that renders as one), whose
        // contents only mean something together. Tabs, panels and slideouts are not
        // here: those merge by name, so work added inside them can move.
        private static readonly string[] BundleSuffixes =
        {
            ".pushbutton", ".toggle", ".smartbutton", ".nobutton", ".urlbutton", ".linkbutton",
            ".dockpane", ".stack", ".pulldown", ".splitbutton", ".splitpushbutton",
        };

        public sealed class Change
        {
            public Change(string path, bool added)
            {
                Path = Key(path);
                Added = added;
            }

            /// <summary>Extension-relative path, forward slashes.</summary>
            public string Path { get; }

            /// <summary>True for a file no release shipped; false for a shipped file whose
            /// content matches no release (someone edited it).</summary>
            public bool Added { get; }
        }

        public sealed class UpgradePlan
        {
            /// <summary>Every changed file: all of it is copied to the backup.</summary>
            public List<string> Backup { get; } = new List<string>();

            /// <summary>Added files that also move to My pyNavis.extension.</summary>
            public List<string> Move { get; } = new List<string>();

            /// <summary>Added files that sit inside a shipped button or stack, so they
            /// cannot live anywhere else: backup only.</summary>
            public List<string> KeptInBackupOnly { get; } = new List<string>();

            /// <summary>Shipped files someone edited; the new version replaces them.</summary>
            public List<string> Edited { get; } = new List<string>();
        }

        /// <summary>
        /// The SHA-256 a manifest records for a file, lower-case hex. Text is hashed with
        /// every CRLF read as LF, because a file's line endings change between the git
        /// tag, a checkout and an install (one field file had an LF line and a CRLF line
        /// and matched neither); a file with a NUL byte is binary and hashed as it is.
        /// </summary>
        public static string ContentHash(byte[] data)
        {
            var bytes = data;
            if (Array.IndexOf(data, (byte)0) < 0)
            {
                var text = new List<byte>(data.Length);
                for (var i = 0; i < data.Length; i++)
                    if (!(data[i] == (byte)'\r' && i + 1 < data.Length && data[i + 1] == (byte)'\n'))
                        text.Add(data[i]);
                bytes = text.ToArray();
            }
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2")));
        }

        /// <summary>Extension-relative path as manifests write it: forward slashes, no leading one.</summary>
        public static string Key(string path) => (path ?? "").Replace('\\', '/').TrimStart('/');

        /// <summary>One manifest line: lower-case hash, two spaces, the path.</summary>
        public static string Line(string sha256, string path) => sha256.ToLowerInvariant() + "  " + Key(path);

        /// <summary>Known hashes per path from manifest lines, added to into when given.
        /// Blank lines and lines starting with # are skipped.</summary>
        public static Dictionary<string, HashSet<string>> Read(IEnumerable<string> lines,
            Dictionary<string, HashSet<string>> into = null)
        {
            var known = into ?? new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in lines)
            {
                var line = (raw ?? "").Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var space = line.IndexOf(' ');
                if (space <= 0) continue;
                var hash = line.Substring(0, space).ToLowerInvariant();
                var path = Key(line.Substring(space).Trim());
                if (path.Length == 0) continue;
                if (!known.TryGetValue(path, out var hashes))
                    known[path] = hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                hashes.Add(hash);
            }
            return known;
        }

        public static bool IsShipped(Dictionary<string, HashSet<string>> known, string path, string sha256) =>
            known.TryGetValue(Key(path), out var hashes) && hashes.Contains(sha256);

        /// <summary>Files nobody made on purpose: Python caches and the manifest itself.</summary>
        public static bool Ignored(string path)
        {
            var key = Key(path);
            return key.Equals(ManifestName, StringComparison.OrdinalIgnoreCase)
                || key.EndsWith(".pyc", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("__pycache__/", StringComparison.OrdinalIgnoreCase)
                || key.IndexOf("/__pycache__/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>The installed files (path to SHA-256) that no release shipped as they are.</summary>
        public static List<Change> Compare(IEnumerable<KeyValuePair<string, string>> installed,
            Dictionary<string, HashSet<string>> known)
        {
            var changes = new List<Change>();
            foreach (var pair in installed)
            {
                if (Ignored(pair.Key)) continue;
                if (!known.TryGetValue(Key(pair.Key), out var hashes))
                    changes.Add(new Change(pair.Key, true));
                else if (!hashes.Contains(pair.Value))
                    changes.Add(new Change(pair.Key, false));
            }
            return changes;
        }

        public static UpgradePlan Plan(IEnumerable<Change> changes, Dictionary<string, HashSet<string>> known)
        {
            var plan = new UpgradePlan();
            foreach (var change in changes)
            {
                plan.Backup.Add(change.Path);
                if (!change.Added) plan.Edited.Add(change.Path);
                else if (InsideShippedBundle(change.Path, known)) plan.KeptInBackupOnly.Add(change.Path);
                else plan.Move.Add(change.Path);
            }
            return plan;
        }

        /// <summary>True when an enclosing folder is a button or stack that a release shipped.</summary>
        private static bool InsideShippedBundle(string path, Dictionary<string, HashSet<string>> known)
        {
            var parts = Key(path).Split('/');
            for (var depth = 1; depth < parts.Length; depth++)
            {
                var folder = parts[depth - 1];
                if (!BundleSuffixes.Any(s => folder.EndsWith(s, StringComparison.OrdinalIgnoreCase))) continue;
                var prefix = string.Join("/", parts, 0, depth) + "/";
                if (known.Keys.Any(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
            return false;
        }
    }
}
