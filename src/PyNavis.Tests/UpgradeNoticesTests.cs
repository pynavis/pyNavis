using System.Collections.Generic;
using PyNavis.Runtime.Upgrade;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The two things the runtime says about users' work in pyNavis.extension: once
    /// after an update, what the installer kept and where; and at startup, before the
    /// next update, that there is work in a folder updates replace.
    /// </summary>
    public class UpgradeNoticesTests
    {
        private static Dictionary<string, object> Report(int moved, int backedUpOnly, int edited) =>
            new Dictionary<string, object>
            {
                ["version"] = "1.2.0",
                ["backup"] = @"C:\Users\x\AppData\Roaming\pyNavis\backups\pyNavis.extension before 1.2.0 (2026-09-29 1432)",
                ["moved"] = Paths(moved),
                ["backedUpOnly"] = Paths(backedUpOnly),
                ["edited"] = Paths(edited),
            };

        private static object[] Paths(int n)
        {
            var list = new object[n];
            for (var i = 0; i < n; i++) list[i] = "file" + i;
            return list;
        }

        [Fact]
        public void AfterAnUpdate_SaysWhatMovedAndWhereTheBackupIs()
        {
            var (title, detail) = UpgradeNotices.ReportMessage(Report(3, 0, 0));
            Assert.Equal("pyNavis 1.2.0 kept your work", title);
            Assert.Equal("3 files you had added to pyNavis.extension moved to My pyNavis.extension. " +
                         "A copy of everything is in backups\\pyNavis.extension before 1.2.0 (2026-09-29 1432).", detail);
        }

        [Fact]
        public void AfterAnUpdate_NamesEditsAndFilesThatCouldOnlyBeBackedUp()
        {
            var (_, detail) = UpgradeNotices.ReportMessage(Report(1, 2, 4));
            Assert.Equal("1 file you had added to pyNavis.extension moved to My pyNavis.extension. " +
                         "2 added inside pyNavis buttons and 4 pyNavis files you had edited are only in the backup. " +
                         "A copy of everything is in backups\\pyNavis.extension before 1.2.0 (2026-09-29 1432).", detail);
        }

        [Fact]
        public void AtStartup_WarnsBeforeTheNextUpdate()
        {
            var (title, detail) = UpgradeNotices.ChangesMessage(added: 2, edited: 1);
            Assert.Equal("You have work inside pyNavis.extension", title);
            Assert.Equal("2 added and 1 edited file. Updates replace that folder: what you added moves to " +
                         "My pyNavis.extension and edits are only backed up. Keep your own tools in My pyNavis.extension.",
                detail);
        }

        [Fact]
        public void Fingerprint_ChangesOnlyWhenTheWorkDoes()
        {
            // The startup warning shows once per state of the folder, not every start.
            var a = UpgradeNotices.Fingerprint(new[] { new ShippedFiles.Change("b.py", true), new ShippedFiles.Change("a.py", false) });
            var b = UpgradeNotices.Fingerprint(new[] { new ShippedFiles.Change("a.py", false), new ShippedFiles.Change("b.py", true) });
            var c = UpgradeNotices.Fingerprint(new[] { new ShippedFiles.Change("a.py", false) });
            Assert.Equal(a, b);
            Assert.NotEqual(a, c);
        }
    }
}
