using System;
using System.IO;
using PyNavis.Runtime.Engine;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// Reload must drop cached modules under given roots (pynavislib) on every created
    /// engine, so library edits are picked up without restarting Navisworks. Config
    /// (base) search-path modules stay cached across runs by design - only an explicit
    /// invalidation refreshes them. Only this class may touch the global EngineManager.
    /// </summary>
    public class EngineManagerInvalidationTests : IDisposable
    {
        private readonly string _libDir;

        public EngineManagerInvalidationTests()
        {
            _libDir = Path.Combine(Path.GetTempPath(), "pynavis_tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_libDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_libDir, true); } catch { }
        }

        [Fact]
        public void InvalidateModules_DropsCachedModules_UnderGivenRoots()
        {
            var config = new EngineConfig();
            var stdlib = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(stdlib)) config.SearchPaths.Add(stdlib);
            config.SearchPaths.Add(_libDir);
            EngineManager.Configure(config);
            var engine = EngineManager.GetEngine();

            var mod = Path.Combine(_libDir, "m_libcache.py");
            File.WriteAllText(mod, "VALUE = 'first'\n");

            var collected = new System.Collections.Generic.List<string>();
            Assert.True(Execute(engine, collected), "run1 failed");

            File.WriteAllText(mod, "VALUE = 'second'\n");
            Assert.True(Execute(engine, collected), "run2 failed");
            // precondition: base-path modules are cached, the edit is NOT visible yet
            Assert.Equal(new[] { "first", "first" }, collected);

            EngineManager.InvalidateModules(new[] { _libDir });

            Assert.True(Execute(engine, collected), "run3 failed");
            Assert.Equal(new[] { "first", "first", "second" }, collected);
        }

        private static bool Execute(IEngine engine, System.Collections.Generic.List<string> collected)
        {
            var req = new ScriptRequest { Code = "import m_libcache\n__collected__.Add(m_libcache.VALUE)" };
            req.Globals["__collected__"] = collected;
            return engine.Execute(req).Succeeded;
        }

        [Fact]
        public void GetEngine_CPython_ReturnsInitializedCPythonEngine()
        {
            // The "cpython" id must resolve to a working engine, not
            // NotSupportedException. bundle.yaml "engine: cpython" rides on this.
            var engine = EngineManager.GetEngine("cpython");

            Assert.Equal("cpython", engine.Id);
            var outw = new StringWriter();
            var r = engine.Execute(new ScriptRequest
            {
                Code = "import sys\nprint('impl=' + sys.implementation.name)",
                Output = outw,
            });
            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("impl=cpython", outw.ToString());
        }
    }
}
