using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using PyNavis.Cli.Upgrade;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// What the installer runs (pynavis protect-extension) before it replaces
    /// pyNavis.extension: a user's additions and edits are copied to a dated backup,
    /// and additions that stand on their own move to My pyNavis.extension, before a
    /// single file is deleted.
    /// </summary>
    public class ExtensionGuardTests : IDisposable
    {
        private readonly string _root;
        private readonly string _ext;
        private readonly string _manifests;
        private readonly string _backups;
        private readonly string _mine;
        private static readonly DateTime Now = new DateTime(2026, 9, 29, 14, 32, 0);

        private const string ClearClash = "pyNavis.tab/Clash.panel/Clear_Clash.pushbutton/script.py";
        private const string Notes = "pyNavis.tab/Clash.panel/Clear_Clash.pushbutton/notes.txt";
        private const string Mine = "pyNavis.tab/Clash.panel/Mine.pushbutton/script.py";
        private const string FaceMove = "lib/facemove.py";

        public ExtensionGuardTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "pynavis_guard_" + Guid.NewGuid().ToString("N"));
            _ext = Path.Combine(_root, "extensions", "pyNavis.extension");
            _manifests = Path.Combine(_root, "manifests");
            _backups = Path.Combine(_root, "backups");
            _mine = Path.Combine(_root, "extensions", ExtensionGuard.UserExtensionName);
            Directory.CreateDirectory(_manifests);

            Write(_ext, ClearClash, "shipped\r\n");
            Write(_ext, FaceMove, "edited");
            Write(_ext, Mine, "mine");
            Write(_ext, Notes, "notes");
            File.WriteAllLines(Path.Combine(_manifests, "1.1.0.txt"), new[]
            {
                "# pyNavis 1.1.0",
                Hash("shipped\r\n") + "  " + ClearClash,
                Hash("original") + "  " + FaceMove,
            });
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private static void Write(string dir, string rel, string text)
        {
            var path = Path.Combine(dir, rel.Replace('/', '\\'));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        private static string Read(string dir, string rel) =>
            File.ReadAllText(Path.Combine(dir, rel.Replace('/', '\\')));

        private static string Hash(string text) => ShippedFiles.ContentHash(new UTF8Encoding(false).GetBytes(text));

        private GuardReport Run(bool dryRun = false) =>
            ExtensionGuard.Run(_ext, Directory.GetFiles(_manifests), _backups, _mine, "1.2.0", Now, dryRun, TextWriter.Null);

        [Fact]
        public void BacksUpEveryChange_AndMovesWhatStandsOnItsOwn()
        {
            var report = Run();

            Assert.Equal(new[] { Mine }, report.Moved.ToArray());
            Assert.Equal(new[] { Notes }, report.BackedUpOnly.ToArray());
            Assert.Equal(new[] { FaceMove }, report.Edited.ToArray());

            var backup = Path.Combine(_backups, "pyNavis.extension before 1.2.0 (2026-09-29 1432)");
            Assert.Equal(backup, report.BackupDir);
            Assert.Equal("mine", Read(backup, Mine));
            Assert.Equal("notes", Read(backup, Notes));
            Assert.Equal("edited", Read(backup, FaceMove));
            Assert.False(File.Exists(Path.Combine(backup, ClearClash.Replace('/', '\\'))));   // shipped: not copied
            Assert.Contains("My pyNavis.extension", File.ReadAllText(Path.Combine(backup, "README.txt")));

            Assert.Equal("mine", Read(_mine, Mine));
            Assert.False(File.Exists(Path.Combine(_mine, Notes.Replace('/', '\\'))));
            Assert.True(File.Exists(Path.Combine(_ext, Mine.Replace('/', '\\'))));        // the installer deletes, not us
        }

        [Fact]
        public void NeverOverwritesWhatIsAlreadyInMyExtension()
        {
            Write(_mine, Mine, "older");

            var report = Run();

            Assert.Equal("older", Read(_mine, Mine));
            Assert.Equal(new[] { Mine }, report.Conflicts.ToArray());
            Assert.Equal("mine", Read(report.BackupDir, Mine));                           // still safe in the backup
        }

        [Fact]
        public void TheExtensionsOwnManifest_CountsAsShipped()
        {
            // An extension installed by 1.2.0 or later lists its own files.
            File.WriteAllLines(Path.Combine(_ext, ".pynavis-manifest"), new[] { Hash("notes") + "  " + Notes });

            var report = Run();

            Assert.DoesNotContain(Notes, report.BackedUpOnly);
        }

        [Fact]
        public void NothingChanged_WritesNothing()
        {
            File.Delete(Path.Combine(_ext, Mine.Replace('/', '\\')));
            File.Delete(Path.Combine(_ext, Notes.Replace('/', '\\')));
            Write(_ext, FaceMove, "original");

            var report = Run();

            Assert.True(report.Nothing);
            Assert.Null(report.BackupDir);
            Assert.False(Directory.Exists(_backups));
            Assert.False(Directory.Exists(_mine));
        }

        [Fact]
        public void DryRun_ReportsThePlan_AndTouchesNothing()
        {
            var report = Run(dryRun: true);

            Assert.Equal(new[] { Mine }, report.Moved.ToArray());
            Assert.False(Directory.Exists(_backups));
            Assert.False(Directory.Exists(_mine));
        }

        [Fact]
        public void NoExtension_NothingToDo()
        {
            Directory.Delete(_ext, true);
            Assert.True(Run().Nothing);
        }

        [Fact]
        public void Report_RoundTripsAsJson_ForTheRuntimeToShowOnce()
        {
            var report = Run();
            var path = Path.Combine(_root, "last-upgrade.json");

            ExtensionGuard.WriteReport(report, path);
            var text = File.ReadAllText(path);

            Assert.Contains("\"version\":\"1.2.0\"", text);
            Assert.Contains(Mine, text);
        }

        [Fact]
        public void Command_ParsesItsFlags_WritesTheReport_AndReturnsZero()
        {
            var report = Path.Combine(_root, "last-upgrade.json");
            var code = ExtensionGuard.Command(new[]
            {
                "protect-extension", "--extension", _ext, "--manifests", _manifests,
                "--backup-root", _backups, "--user-extension", _mine, "--version", "1.2.0",
                "--report", report,
            }, Now, TextWriter.Null);

            Assert.Equal(0, code);
            Assert.True(File.Exists(report));
            Assert.Equal("mine", Read(_mine, Mine));
        }

        [Fact]
        public void Command_TakesOneManifestFile_AsWellAsAFolderOfThem()
        {
            // The installer packs every release's manifest into one file.
            var code = ExtensionGuard.Command(new[]
            {
                "protect-extension", "--extension", _ext, "--manifests", Path.Combine(_manifests, "1.1.0.txt"),
                "--backup-root", _backups, "--user-extension", _mine, "--version", "1.2.0",
            }, Now, TextWriter.Null);

            Assert.Equal(0, code);
            Assert.Equal("mine", Read(_mine, Mine));
            Assert.False(File.Exists(Path.Combine(_backups, "pyNavis.extension before 1.2.0 (2026-09-29 1432)",
                ClearClash.Replace('/', '\\'))));                                      // recognised as shipped
        }

        [Fact]
        public void Command_WithoutItsFlags_IsAUsageError()
        {
            Assert.Equal(2, ExtensionGuard.Command(new[] { "protect-extension", "--extension", _ext }, Now, TextWriter.Null));
        }

        [Fact]
        public void Command_FallsBackToAFullBackup_WhenAManifestCannotBeRead()
        {
            // A plan that cannot be made must never cost the user their work: the
            // whole folder is copied instead, and the install goes on.
            int code;
            using (File.Open(Path.Combine(_manifests, "1.1.0.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                code = ExtensionGuard.Command(new[]
                {
                    "protect-extension", "--extension", _ext, "--manifests", _manifests,
                    "--backup-root", _backups, "--user-extension", _mine, "--version", "1.2.0",
                }, Now, TextWriter.Null);
            }

            Assert.Equal(0, code);
            Assert.True(Directory.Exists(Path.Combine(_backups, "pyNavis.extension before 1.2.0 (2026-09-29 1432) full")));
        }

        [Fact]
        public void LineEndingsAlone_AreNotAChange()
        {
            // The release had LF, the install CRLF (or a mix): still the shipped file.
            Write(_ext, ClearClash, "shipped\n");
            Assert.DoesNotContain(ClearClash, Run().Edited);
        }

        [Fact]
        public void WriteManifest_ListsTheExtension_SoTheSameFilesLaterReadAsShipped()
        {
            // What package.ps1 runs on the staged extension of the release it builds.
            var manifest = Path.Combine(_root, "1.2.0.txt");
            Assert.Equal(0, ExtensionGuard.Command(new[] { "write-manifest", _ext, manifest, "--version", "1.2.0" },
                Now, TextWriter.Null));

            var lines = File.ReadAllLines(manifest);
            Assert.Equal("# pyNavis 1.2.0", lines[0]);
            Assert.Contains(Hash("mine") + "  " + Mine, lines);

            var report = ExtensionGuard.Run(_ext, new[] { manifest }, _backups, _mine, "1.2.1", Now, false, TextWriter.Null);
            Assert.True(report.Nothing);
        }

        [Fact]
        public void BackupEverything_IsTheFallback_WhenThePlanCannotBeMade()
        {
            var dir = ExtensionGuard.BackupEverything(_ext, _backups, "1.2.0", Now);

            Assert.EndsWith("pyNavis.extension before 1.2.0 (2026-09-29 1432) full", dir);
            Assert.Equal("shipped\r\n", Read(dir, ClearClash));
            Assert.Equal("mine", Read(dir, Mine));
        }
    }
}
