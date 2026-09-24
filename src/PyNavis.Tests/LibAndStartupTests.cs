using System;
using System.IO;
using PyNavis.Runtime.Bundles;
using Xunit;

namespace PyNavis.Tests
{
    public class LibAndStartupTests : IDisposable
    {
        private readonly string _root;

        public LibAndStartupTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "pynavis_tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        [Fact]
        public void FindLibraries_Returns_Lib_Folders_Sorted()
        {
            Directory.CreateDirectory(Path.Combine(_root, "B.lib"));
            Directory.CreateDirectory(Path.Combine(_root, "A.lib"));
            Directory.CreateDirectory(Path.Combine(_root, "C.extension"));
            var libs = BundleParser.FindLibraries(_root);
            Assert.Equal(2, libs.Count);
            Assert.EndsWith("A.lib", libs[0]);
            Assert.EndsWith("B.lib", libs[1]);
        }

        [Fact]
        public void FindLibraries_Handles_Missing_Root()
        {
            Assert.Empty(BundleParser.FindLibraries(Path.Combine(_root, "nope")));
            Assert.Empty(BundleParser.FindLibraries(null));
        }

        [Fact]
        public void StartupScript_Is_Discovered()
        {
            var ext = Path.Combine(_root, "E.extension");
            Directory.CreateDirectory(ext);
            File.WriteAllText(Path.Combine(ext, "startup.py"), "pass\n");
            Assert.EndsWith("startup.py", BundleParser.ParseExtension(ext).StartupScriptPath);
        }

        [Fact]
        public void No_StartupScript_Means_Null()
        {
            var ext = Path.Combine(_root, "E.extension");
            Directory.CreateDirectory(ext);
            Assert.Null(BundleParser.ParseExtension(ext).StartupScriptPath);
        }
    }
}
