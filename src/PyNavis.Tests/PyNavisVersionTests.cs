using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using PyNavis.Runtime;
using PyNavis.Runtime.Config;
using PyNavis.Runtime.Forms;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// One version number, stamped into every assembly from pynavis/__init__.py at
    /// build, and shown wherever someone debugging an install would look: the boot
    /// log, the Settings window, and the host object scripts see. The number is plain
    /// MAJOR.MINOR.PATCH and CHANGELOG.md leads with it, so a release cannot be built
    /// without its notes.
    /// </summary>
    public class PyNavisVersionTests
    {
        private static string RepoRoot()
        {
            for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "pynavislib", "pynavis", "__init__.py")))
                    return dir.FullName;
            throw new FileNotFoundException("pynavis/__init__.py not found above the test dir");
        }

        private static string RepoVersion()
        {
            var init = Path.Combine(RepoRoot(), "pynavislib", "pynavis", "__init__.py");
            return Regex.Match(File.ReadAllText(init), @"__version__\s*=\s*['""]([^'""]+)").Groups[1].Value;
        }

        /// <summary>The first "## [x.y.z] - yyyy-mm-dd" heading in CHANGELOG.md.</summary>
        private static Match FirstReleaseHeading()
        {
            var changelog = Path.Combine(RepoRoot(), "CHANGELOG.md");
            Assert.True(File.Exists(changelog), "CHANGELOG.md is missing from the repo root");
            var match = Regex.Match(File.ReadAllText(changelog),
                @"^## \[(?<version>[^\]]+)\] - (?<date>\S+)\s*$", RegexOptions.Multiline);
            Assert.True(match.Success, "CHANGELOG.md has no '## [x.y.z] - yyyy-mm-dd' heading");
            return match;
        }

        [Fact]
        public void TheProductVersion_IsTheOneInTheLibrary()
        {
            Assert.Equal(RepoVersion(), PyNavisVersion.Product);
        }

        /// <summary>Autodesk's PackageContents.xml and the installer's version resource
        /// both take digits and dots only, so no -beta or +build suffix ever goes in.</summary>
        [Fact]
        public void TheVersion_IsPlainMajorMinorPatch()
        {
            Assert.Matches(@"^\d+\.\d+\.\d+$", RepoVersion());
        }

        [Fact]
        public void TheChangelog_LeadsWithTheCurrentVersion()
        {
            Assert.Equal(RepoVersion(), FirstReleaseHeading().Groups["version"].Value);
        }

        [Fact]
        public void TheChangelog_DatesTheCurrentRelease()
        {
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", FirstReleaseHeading().Groups["date"].Value);
        }

        [Fact]
        public void TheSummary_NamesVersionHostAndRuntimeFolder()
        {
            var summary = PyNavisVersion.Summary();

            Assert.StartsWith("pyNavis " + PyNavisVersion.Product, summary);
            Assert.Contains("Navisworks", summary);
            Assert.Contains("PyNavis.Runtime.dll", summary);
        }

        [Fact]
        public void TheSettingsWindow_ShowsTheVersionLine()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = SettingsDialog.Build(new PyNavisConfig.UserSettings());
                    Assert.Contains(PyNavisVersion.Product, SettingsDialog.VersionLineOf(window));
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }
    }
}
