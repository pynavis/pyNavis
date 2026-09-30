using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Threading;
using PyNavis.Runtime.Bundles;

namespace PyNavis.Runtime.Upgrade
{
    /// <summary>
    /// What the runtime says about users' work in pyNavis.extension, the folder every
    /// update replaces. Once after an update: what the installer's guard (pynavis
    /// protect-extension) kept, and where. At startup, before the next update: that
    /// there is work there, once per state of the folder. The words and the
    /// fingerprint are pure; the two Show/Warn methods read files and toast.
    /// </summary>
    public static class UpgradeNotices
    {
        /// <summary>Written by the installer's guard, shown and deleted at the next start.</summary>
        public const string ReportFile = "last-upgrade.json";

        /// <summary>The fingerprint of the work last warned about, so a warning shows once.</summary>
        public const string SeenFile = "extension-changes.seen";

        public static (string title, string detail) ReportMessage(IDictionary<string, object> report)
        {
            var version = report.TryGetValue("version", out var v) ? v as string : null;
            var moved = Count(report, "moved");
            var only = Count(report, "backedUpOnly");
            var edited = Count(report, "edited");
            var backup = report.TryGetValue("backup", out var b) ? b as string : null;

            var parts = new List<string>();
            if (moved > 0)
                parts.Add($"{moved} {Files(moved)} you had added to pyNavis.extension moved to My pyNavis.extension.");
            var kept = new List<string>();
            if (only > 0) kept.Add(only == 1 ? "1 added inside a pyNavis button" : $"{only} added inside pyNavis buttons");
            if (edited > 0) kept.Add($"{edited} pyNavis {Files(edited)} you had edited");
            if (kept.Count > 0)
                parts.Add(string.Join(" and ", kept) + (only + edited == 1 ? " is" : " are") + " only in the backup.");
            if (!string.IsNullOrEmpty(backup))
                parts.Add($"A copy of everything is in {ShortBackup(backup)}.");
            return ($"pyNavis {version} kept your work", string.Join(" ", parts));
        }

        public static (string title, string detail) ChangesMessage(int added, int edited)
        {
            string what;
            if (added > 0 && edited > 0) what = $"{added} added and {edited} edited {Files(edited)}.";
            else if (added > 0) what = $"{added} added {Files(added)}.";
            else what = $"{edited} edited {Files(edited)}.";
            return ("You have work inside pyNavis.extension",
                what + " Updates replace that folder: what you added moves to My pyNavis.extension and " +
                "edits are only backed up. Keep your own tools in My pyNavis.extension.");
        }

        /// <summary>The same work gives the same fingerprint, whatever order it was found in.</summary>
        public static string Fingerprint(IEnumerable<ShippedFiles.Change> changes)
        {
            var text = string.Join("\n", changes
                .Select(c => (c.Added ? "A:" : "E:") + c.Path.ToLowerInvariant())
                .OrderBy(s => s, StringComparer.Ordinal));
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(text)).Select(x => x.ToString("x2")));
        }

        // ---- the runtime side -------------------------------------------------------

        /// <summary>The installer's report, shown once, then deleted.</summary>
        public static void ShowLastUpgradeReport(string appDataDir)
        {
            var path = Path.Combine(appDataDir, ReportFile);
            if (!File.Exists(path)) return;
            try
            {
                var report = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
                var (title, detail) = ReportMessage(report);
                Log.Info($"Upgrade report: {title}. {detail}");
                Forms.Toast.Show("info", title, detail);
            }
            catch (Exception ex)
            {
                Log.Error("Could not read the upgrade report " + path, ex);
            }
            finally
            {
                try { File.Delete(path); } catch (Exception ex) { Log.Error("Could not delete " + path, ex); }
            }
        }

        /// <summary>
        /// When the installed pyNavis.extension (the one carrying its release's manifest)
        /// holds work of the user's, warns once per state of it. The files are hashed on a
        /// worker thread; the toast goes back to the UI thread this was called on.
        /// </summary>
        public static void WarnAboutWorkInShippedExtension(IReadOnlyList<ExtensionModel> extensions, string appDataDir)
        {
            var shipped = extensions.FirstOrDefault(e =>
                string.Equals(e.Name, RibbonMerge.ShippedExtension, StringComparison.OrdinalIgnoreCase));
            if (shipped == null) return;
            var manifest = Path.Combine(shipped.Directory, ShippedFiles.ManifestName);
            if (!File.Exists(manifest)) return;            // a dev checkout: nothing shipped to compare with

            var ui = Dispatcher.CurrentDispatcher;
            var seenPath = Path.Combine(appDataDir, SeenFile);
            Task.Run(() =>
            {
                try
                {
                    var known = ShippedFiles.Read(File.ReadAllLines(manifest));
                    var root = shipped.Directory.TrimEnd('\\', '/');
                    var installed = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                        .Select(p => new KeyValuePair<string, string>(
                            ShippedFiles.Key(p.Substring(root.Length)), ShippedFiles.ContentHash(File.ReadAllBytes(p))));
                    var changes = ShippedFiles.Compare(installed, known);
                    if (changes.Count == 0) return;

                    var fingerprint = Fingerprint(changes);
                    if (File.Exists(seenPath) && File.ReadAllText(seenPath).Trim() == fingerprint) return;
                    File.WriteAllText(seenPath, fingerprint);

                    var (title, detail) = ChangesMessage(changes.Count(c => c.Added), changes.Count(c => !c.Added));
                    Log.Info($"{title}: {string.Join(", ", changes.Select(c => c.Path))}");
                    ui.BeginInvoke(new Action(() => Forms.Toast.Show("warning", title, detail)));
                }
                catch (Exception ex)
                {
                    Log.Error("Could not check pyNavis.extension for work of the user's", ex);
                }
            });
        }

        private static int Count(IDictionary<string, object> report, string key) =>
            report.TryGetValue(key, out var value) && value is ICollection list ? list.Count : 0;

        private static string Files(int n) => n == 1 ? "file" : "files";

        /// <summary>"backups\pyNavis.extension before ..." rather than the whole path.</summary>
        private static string ShortBackup(string path)
        {
            var trimmed = path.TrimEnd('\\', '/');
            var parent = Path.GetFileName(Path.GetDirectoryName(trimmed));
            return string.IsNullOrEmpty(parent) ? Path.GetFileName(trimmed) : parent + "\\" + Path.GetFileName(trimmed);
        }
    }
}
