using System;
using System.IO;
using System.Linq;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The plan's engine-seam rule, enforced: nothing outside the one concrete engine
    /// file may touch that engine's hosting types. This is what keeps engines swappable
    /// and the runtime loadable even if one engine's dependencies are absent.
    /// </summary>
    public class EngineSeamEnforcementTests
    {
        private static string RuntimeSrcDir { get; } = FindRuntimeSrc();

        private static string FindRuntimeSrc()
        {
            for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "src", "PyNavis.Runtime");
                if (Directory.Exists(candidate)) return candidate;
            }
            throw new DirectoryNotFoundException("src\\PyNavis.Runtime not found above test dir");
        }

        private static string[] SourcesReferencing(string pattern, string allowedFile)
        {
            return Directory.GetFiles(RuntimeSrcDir, "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                .Where(p => !Path.GetFileName(p).Equals(allowedFile, StringComparison.OrdinalIgnoreCase))
                // "IronPython.Runtime" contains "Python.Runtime": blank it out so the
                // pythonnet guard does not fire on an IronPython namespace, while the
                // IronPython guard (which searches for it directly) still sees it.
                .Where(p => (pattern == "Python.Runtime"
                        ? File.ReadAllText(p).Replace("IronPython.Runtime", "")
                        : File.ReadAllText(p)).Contains(pattern))
                .Select(Path.GetFileName)
                .ToArray();
        }

        [Fact]
        public void IronPythonTypes_OnlyIn_IronPythonEngineFile()
        {
            // Library namespaces only: our own IronPythonEngine class name (and prose
            // mentioning IronPython) may legitimately appear elsewhere.
            Assert.Empty(SourcesReferencing("IronPython.Hosting", "IronPythonEngine.cs"));
            Assert.Empty(SourcesReferencing("IronPython.Runtime", "IronPythonEngine.cs"));
            Assert.Empty(SourcesReferencing("Microsoft.Scripting", "IronPythonEngine.cs"));
        }

        [Fact]
        public void PythonnetTypes_OnlyIn_CPythonEngineFile()
        {
            Assert.Empty(SourcesReferencing("Python.Runtime", "CPythonEngine.cs"));
        }
    }
}
