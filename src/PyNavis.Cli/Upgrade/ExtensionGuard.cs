using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace PyNavis.Cli.Upgrade
{
    /// <summary>What protect-extension did (or, on a dry run, would do).</summary>
    public sealed class GuardReport
    {
        public string Version { get; set; }

        /// <summary>The dated backup folder, or null when there was nothing to keep.</summary>
        public string BackupDir { get; set; }

        /// <summary>Added files now also in My pyNavis.extension, where they keep working.</summary>
        public List<string> Moved { get; } = new List<string>();

        /// <summary>Added files that sit inside a shipped button or stack: in the backup only.</summary>
        public List<string> BackedUpOnly { get; } = new List<string>();

        /// <summary>Shipped files someone edited: in the backup, replaced by the new version.</summary>
        public List<string> Edited { get; } = new List<string>();

        /// <summary>Files My pyNavis.extension already had, with other content: left alone
        /// there, and the newer copy is in the backup.</summary>
        public List<string> Conflicts { get; } = new List<string>();

        public bool Nothing => Moved.Count == 0 && BackedUpOnly.Count == 0 && Edited.Count == 0;
    }

    /// <summary>
    /// Runs before the installer replaces pyNavis.extension, which it must do whole so a
    /// bundle renamed between releases cannot survive beside its replacement. Anything a
    /// user added or edited there (ShippedFiles tells which) is copied to a dated backup
    /// first, and added work that stands on its own is copied into My
    /// pyNavis.extension, where it keeps its place on the ribbon because tabs and panels
    /// merge by name. Nothing here deletes; the installer does, afterwards.
    /// </summary>
    public static class ExtensionGuard
    {
        public const string UserExtensionName = "My pyNavis.extension";

        public static GuardReport Run(string extensionDir, IEnumerable<string> manifestFiles, string backupRoot,
            string userExtensionDir, string newVersion, DateTime now, bool dryRun, TextWriter log)
        {
            var report = new GuardReport { Version = newVersion };
            if (!Directory.Exists(extensionDir)) return report;

            var known = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in manifestFiles ?? Enumerable.Empty<string>())
                ShippedFiles.Read(File.ReadAllLines(file), known);
            var own = Path.Combine(extensionDir, ShippedFiles.ManifestName);
            if (File.Exists(own)) ShippedFiles.Read(File.ReadAllLines(own), known);

            var installed = Directory.GetFiles(extensionDir, "*", SearchOption.AllDirectories)
                .Select(path => new KeyValuePair<string, string>(Relative(extensionDir, path), Hash(path)));
            var plan = ShippedFiles.Plan(ShippedFiles.Compare(installed, known), known);
            report.Moved.AddRange(plan.Move);
            report.BackedUpOnly.AddRange(plan.KeptInBackupOnly);
            report.Edited.AddRange(plan.Edited);
            if (report.Nothing) return report;

            report.BackupDir = FreeName(Path.Combine(backupRoot, BackupName(newVersion, now)));
            log.WriteLine($"pyNavis.extension has {plan.Backup.Count} file(s) of your own: backing up to {report.BackupDir}");
            if (dryRun) return report;

            foreach (var rel in plan.Backup)
                CopyInto(extensionDir, rel, report.BackupDir);
            foreach (var rel in plan.Move)
            {
                var target = Full(userExtensionDir, rel);
                if (File.Exists(target) && Hash(target) != Hash(Full(extensionDir, rel)))
                {
                    report.Conflicts.Add(rel);
                    continue;
                }
                CopyInto(extensionDir, rel, userExtensionDir);
            }
            File.WriteAllText(Path.Combine(report.BackupDir, "README.txt"), Describe(report, userExtensionDir),
                new UTF8Encoding(false));
            return report;
        }

        public const string Usage =
            "pynavis protect-extension --extension <dir> --manifests <dir or file> --backup-root <dir> " +
            "--user-extension <dir> --version <x.y.z> [--report <file>] [--dry-run]";

        /// <summary>
        /// The installer's entry point. 0 when the user's work is safe (backed up, moved,
        /// or there was none), 2 for a usage error, 3 when not even the full backup could
        /// be made, which is the one case the installer stops for: replacing the folder
        /// then could lose work.
        /// </summary>
        public static int Command(string[] args, DateTime now, TextWriter log)
        {
            if (args.Length > 0 && args[0] == "write-manifest")
            {
                // write-manifest <extension dir> <out file> [--version x.y.z]
                if (args.Length < 3)
                {
                    log.WriteLine("pynavis write-manifest <extension dir> <out file> [--version x.y.z]");
                    return 2;
                }
                var version = args.Length >= 5 && args[3] == "--version" ? args[4] : "";
                WriteManifest(args[1], args[2], version);
                log.WriteLine("Wrote " + args[2]);
                return 0;
            }

            var flags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var dryRun = false;
            for (var i = 1; i < args.Length; i++)
            {
                if (args[i] == "--dry-run") dryRun = true;
                else if (args[i].StartsWith("--", StringComparison.Ordinal) && i + 1 < args.Length)
                    flags[args[i].Substring(2)] = args[++i];
            }
            foreach (var required in new[] { "extension", "manifests", "backup-root", "user-extension", "version" })
            {
                if (flags.ContainsKey(required)) continue;
                log.WriteLine("Missing --" + required + ".");
                log.WriteLine(Usage);
                return 2;
            }

            try
            {
                // A folder of per-release manifests, or one file holding them all.
                var manifests = Directory.Exists(flags["manifests"]) ? Directory.GetFiles(flags["manifests"], "*.txt")
                    : File.Exists(flags["manifests"]) ? new[] { flags["manifests"] }
                    : new string[0];
                var report = Run(flags["extension"], manifests, flags["backup-root"], flags["user-extension"],
                    flags["version"], now, dryRun, log);
                if (flags.TryGetValue("report", out var reportPath) && !report.Nothing && !dryRun)
                    WriteReport(report, reportPath);
                log.WriteLine(report.Nothing
                    ? "pyNavis.extension holds nothing of yours."
                    : $"Moved {report.Moved.Count}, backed up only {report.BackedUpOnly.Count}, edited {report.Edited.Count}.");
                return 0;
            }
            catch (Exception ex)
            {
                log.WriteLine("Could not tell your files from pyNavis's (" + ex.Message + "): backing up everything.");
                try
                {
                    if (!dryRun && Directory.Exists(flags["extension"]))
                        log.WriteLine("Backed up to " + BackupEverything(flags["extension"], flags["backup-root"], flags["version"], now));
                    return 0;
                }
                catch (Exception inner)
                {
                    log.WriteLine("The backup failed too: " + inner.Message);
                    return 3;
                }
            }
        }

        /// <summary>The fallback when no plan can be made (an unreadable manifest, say):
        /// the whole extension, as it is, into a "full" backup. Returns that folder.</summary>
        public static string BackupEverything(string extensionDir, string backupRoot, string newVersion, DateTime now)
        {
            var dir = FreeName(Path.Combine(backupRoot, BackupName(newVersion, now) + " full"));
            foreach (var path in Directory.GetFiles(extensionDir, "*", SearchOption.AllDirectories))
                CopyInto(extensionDir, Relative(extensionDir, path), dir);
            return dir;
        }

        /// <summary>The report as JSON, for the runtime to show once at the next start.</summary>
        public static void WriteReport(GuardReport report, string path)
        {
            var data = new Dictionary<string, object>
            {
                ["version"] = report.Version,
                ["backup"] = report.BackupDir,
                ["moved"] = report.Moved,
                ["backedUpOnly"] = report.BackedUpOnly,
                ["edited"] = report.Edited,
                ["conflicts"] = report.Conflicts,
            };
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, new JavaScriptSerializer().Serialize(data), new UTF8Encoding(false));
        }

        /// <summary>A file's manifest hash (ShippedFiles.ContentHash: line endings aside).</summary>
        public static string Hash(string path) => ShippedFiles.ContentHash(File.ReadAllBytes(path));

        /// <summary>The manifest of an extension as it stands: what package.ps1 writes for
        /// the release it builds, into the staged extension and the installer's history.</summary>
        public static void WriteManifest(string extensionDir, string outFile, string version)
        {
            var lines = new List<string> { "# pyNavis " + version };
            lines.AddRange(Directory.GetFiles(extensionDir, "*", SearchOption.AllDirectories)
                .Select(path => Relative(extensionDir, path))
                .Where(rel => !ShippedFiles.Ignored(rel))
                .OrderBy(rel => rel, StringComparer.OrdinalIgnoreCase)
                .Select(rel => ShippedFiles.Line(Hash(Full(extensionDir, rel)), rel)));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outFile)));
            File.WriteAllText(outFile, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
        }

        private static string BackupName(string version, DateTime now) =>
            $"pyNavis.extension before {version} ({now:yyyy-MM-dd HHmm})";

        private static string FreeName(string dir)
        {
            var candidate = dir;
            for (var n = 2; Directory.Exists(candidate); n++)
                candidate = dir + " " + n;
            return candidate;
        }

        private static string Relative(string root, string path) =>
            ShippedFiles.Key(path.Substring(root.TrimEnd('\\', '/').Length));

        private static string Full(string root, string rel) => Path.Combine(root, rel.Replace('/', '\\'));

        private static void CopyInto(string fromRoot, string rel, string toRoot)
        {
            var target = Full(toRoot, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(Full(fromRoot, rel), target, true);
        }

        private static string Describe(GuardReport report, string userExtensionDir)
        {
            var text = new StringBuilder();
            text.AppendLine($"pyNavis {report.Version} replaced pyNavis.extension, which the installer owns.");
            text.AppendLine("This folder keeps what you had added or changed there, as it was.");
            text.AppendLine();
            if (report.Moved.Count > 0)
            {
                text.AppendLine($"Moved to {userExtensionDir} as well, so they stay on the ribbon:");
                foreach (var rel in report.Moved) text.AppendLine("  " + rel);
                text.AppendLine();
            }
            if (report.BackedUpOnly.Count > 0)
            {
                text.AppendLine("Added inside a pyNavis button or stack, so kept here only:");
                foreach (var rel in report.BackedUpOnly) text.AppendLine("  " + rel);
                text.AppendLine();
            }
            if (report.Edited.Count > 0)
            {
                text.AppendLine("pyNavis files you had edited, replaced by the new version:");
                foreach (var rel in report.Edited) text.AppendLine("  " + rel);
                text.AppendLine();
            }
            if (report.Conflicts.Count > 0)
            {
                text.AppendLine("Already in My pyNavis.extension with other content, so left as they were there:");
                foreach (var rel in report.Conflicts) text.AppendLine("  " + rel);
                text.AppendLine();
            }
            text.AppendLine("New buttons and panels belong in My pyNavis.extension: tabs and panels with the");
            text.AppendLine("same names as pyNavis's join them on the ribbon, and updates never touch it.");
            return text.ToString();
        }
    }
}
