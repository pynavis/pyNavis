using System.Collections.Generic;
using System.Linq;
using PyNavis.Runtime.Upgrade;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// Telling a user's own work apart from what the installer shipped, inside
    /// pyNavis.extension: every release lists its files with their SHA-256, and
    /// anything that matches no release is someone's addition or edit. The plan
    /// then says what can move to "My pyNavis.extension" (and keep working, since
    /// tabs and panels merge by name) and what can only be backed up.
    /// </summary>
    public class ShippedFilesTests
    {
        private static string H(char c) => new string(c, 64);

        private const string ClearClash = "pyNavis.tab/Clash.panel/Clear_Clash.pushbutton/script.py";
        private const string Fit = "pyNavis.tab/Viewpoints.panel/Section.stack/Fit.pushbutton/script.py";
        private const string FaceMove = "lib/facemove.py";

        private static Dictionary<string, HashSet<string>> Known() => ShippedFiles.Read(new[]
        {
            "# pyNavis 1.1.0",
            "",
            H('1') + "  " + ClearClash,
            H('2') + "  " + ClearClash,               // the same file with other line endings
            H('3') + "  " + Fit,
            H('4') + "  " + FaceMove,
        });

        [Fact]
        public void Read_TakesSeveralHashesPerPath_AndMatchesPathsWhateverTheSlashesOrCase()
        {
            var known = Known();
            Assert.Equal(3, known.Count);
            Assert.True(ShippedFiles.IsShipped(known, ClearClash, H('1')));
            Assert.True(ShippedFiles.IsShipped(known, ClearClash.Replace('/', '\\').ToUpperInvariant(), H('2')));
            Assert.False(ShippedFiles.IsShipped(known, ClearClash, H('9')));
        }

        [Fact]
        public void Compare_FindsAddedAndEditedFiles_AndIgnoresCachesAndTheManifest()
        {
            var installed = new Dictionary<string, string>
            {
                [ClearClash] = H('2'),                                                   // shipped
                [FaceMove] = H('9'),                                                 // edited
                ["pyNavis.tab/Clash.panel/Mine.pushbutton/script.py"] = H('a'),       // added
                ["lib/__pycache__/facemove.cpython-312.pyc"] = H('b'),               // a cache
                ["stray.pyc"] = H('c'),
                [ShippedFiles.ManifestName] = H('d'),
            };

            var changes = ShippedFiles.Compare(installed, Known());

            Assert.Equal(2, changes.Count);
            Assert.Contains(changes, c => c.Path == FaceMove && !c.Added);
            Assert.Contains(changes, c => c.Path == "pyNavis.tab/Clash.panel/Mine.pushbutton/script.py" && c.Added);
        }

        [Fact]
        public void Plan_MovesAddedWorkThatStandsOnItsOwn_AndOnlyBacksUpTheRest()
        {
            var added = new[]
            {
                "pyNavis.tab/Clash.panel/Mine.pushbutton/script.py",       // a new button in a shipped panel
                "pyNavis.tab/Clash.panel/Mine.pushbutton/icon.png",
                "pyNavis.tab/New.panel/Other.pushbutton/script.py",        // a new panel
                "pyNavis.tab/pyNavis.panel/More.slideout/Mine.pushbutton/script.py",   // slideouts merge too
                "lib/helpers.py",                                          // a module a new button imports
                "hooks/doc-opened.py",
                "pyNavis.tab/Clash.panel/Clear_Clash.pushbutton/notes.txt",    // inside a shipped button
                "pyNavis.tab/Viewpoints.panel/Section.stack/Mine.pushbutton/script.py",  // inside a shipped stack
            };
            var changes = added.Select(p => new ShippedFiles.Change(p, true))
                .Concat(new[] { new ShippedFiles.Change(FaceMove, false) }).ToList();

            var plan = ShippedFiles.Plan(changes, Known());

            Assert.Equal(new[]
            {
                "pyNavis.tab/Clash.panel/Mine.pushbutton/script.py",
                "pyNavis.tab/Clash.panel/Mine.pushbutton/icon.png",
                "pyNavis.tab/New.panel/Other.pushbutton/script.py",
                "pyNavis.tab/pyNavis.panel/More.slideout/Mine.pushbutton/script.py",
                "lib/helpers.py",
                "hooks/doc-opened.py",
            }, plan.Move.ToArray());
            Assert.Equal(new[]
            {
                "pyNavis.tab/Clash.panel/Clear_Clash.pushbutton/notes.txt",
                "pyNavis.tab/Viewpoints.panel/Section.stack/Mine.pushbutton/script.py",
            }, plan.KeptInBackupOnly.ToArray());
            Assert.Equal(new[] { FaceMove }, plan.Edited.ToArray());
            Assert.Equal(changes.Count, plan.Backup.Count);          // everything is backed up
        }

        [Fact]
        public void ContentHash_ReadsTextWhateverItsLineEndings_AndBinaryAsItIs()
        {
            // Found in the field: a file packaged with one LF line and one CRLF line
            // matched neither an LF nor a CRLF copy of the release. Text is compared
            // with every CRLF read as LF; a file with a NUL byte is binary.
            var lf = System.Text.Encoding.UTF8.GetBytes("title: Recall\nshortcut: Ctrl+Shift+R\n");
            var crlf = System.Text.Encoding.UTF8.GetBytes("title: Recall\r\nshortcut: Ctrl+Shift+R\r\n");
            var mixed = System.Text.Encoding.UTF8.GetBytes("title: Recall\nshortcut: Ctrl+Shift+R\r\n");
            Assert.Equal(ShippedFiles.ContentHash(lf), ShippedFiles.ContentHash(crlf));
            Assert.Equal(ShippedFiles.ContentHash(lf), ShippedFiles.ContentHash(mixed));
            // sha256("abc\n"), the text as LF: what `echo abc | sha256sum` prints
            Assert.Equal("edeaaff3f1774ad2888673770c6d64097e391bc362d7d6fb34982ddf0efd18cb",
                ShippedFiles.ContentHash(System.Text.Encoding.ASCII.GetBytes("abc\r\n")));

            var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x0D, 0x0A };
            var pngLf = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0A, 0x1A, 0x0A, 0x00, 0x0A };
            Assert.NotEqual(ShippedFiles.ContentHash(png), ShippedFiles.ContentHash(pngLf));
            Assert.Equal(64, ShippedFiles.ContentHash(png).Length);
        }

        [Fact]
        public void ManifestLine_IsTheHashTwoSpacesAndTheForwardSlashPath()
        {
            Assert.Equal(H('1') + "  lib/facemove.py", ShippedFiles.Line(H('1').ToUpperInvariant(), "lib\\facemove.py"));
        }
    }
}
