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
    /// log, the Settings window, and the host object scripts see.
    /// </summary>
    public class PyNavisVersionTests
    {
        private static string RepoVersion()
        {
            for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var init = Path.Combine(dir.FullName, "pynavislib", "pynavis", "__init__.py");
                if (File.Exists(init))
                    return Regex.Match(File.ReadAllText(init), @"__version__\s*=\s*['""]([^'""]+)").Groups[1].Value;
            }
            throw new FileNotFoundException("pynavis/__init__.py not found above the test dir");
        }

        [Fact]
        public void TheProductVersion_IsTheOneInTheLibrary()
        {
            Assert.Equal(RepoVersion(), PyNavisVersion.Product);
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
