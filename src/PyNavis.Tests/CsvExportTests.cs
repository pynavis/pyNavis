using System;
using System.IO;
using System.Linq;
using System.Text;
using PyNavis.Runtime.Engine;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// extensions/pyNavis.extension/lib/csvexport.py, the writer behind Export
    /// Viewpoints CSV and Clash Report, run through the real IronPython engine
    /// because the engine's own text writer is what it works around: a file
    /// opened with encoding='utf-8-sig' there gets a byte order mark in front of
    /// every write, so every row of a CSV would start with one.
    /// </summary>
    public class CsvExportTests : IDisposable
    {
        private readonly IronPythonEngine _engine;
        private readonly string _path =
            Path.Combine(Path.GetTempPath(), "pynavis_csv_" + Guid.NewGuid().ToString("N") + ".csv");

        public CsvExportTests()
        {
            _engine = new IronPythonEngine();
            var config = new EngineConfig();
            var stdlib = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(stdlib)) config.SearchPaths.Add(stdlib);
            config.SearchPaths.Add(Path.GetFullPath(Path.Combine(
                PyNavisLibTests.PyNavisLibDir, "..", "extensions", "pyNavis.extension", "lib")));
            _engine.Initialize(config);
        }

        public void Dispose()
        {
            if (File.Exists(_path)) File.Delete(_path);
        }

        private string Write(string code)
        {
            var outw = new StringWriter();
            var request = new ScriptRequest { Code = "import csvexport\n" + code, Output = outw };
            request.Globals["path"] = _path;
            var r = _engine.Execute(request);
            Assert.True(r.Succeeded, r.ErrorText);
            return outw.ToString().Trim();
        }

        [Fact]
        public void WriteRows_PutsOneByteOrderMarkAtTheTop_SoExcelReadsUtf8()
        {
            var printed = Write(
                "print(csvexport.write_rows(path, ['Name', 'Folder'],\n" +
                "                           [['\\u5ba4\\u5185 \\u0416', 'A, B'], ['Line\\nbreak', '']]))");

            Assert.Equal("2", printed);
            var bytes = File.ReadAllBytes(_path);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
            var text = new UTF8Encoding(false, true).GetString(bytes, 3, bytes.Length - 3);
            Assert.Equal("Name,Folder\r\n室内 Ж,\"A, B\"\r\n\"Line\nbreak\",\r\n", text);
        }

        [Fact]
        public void WriteRows_TakesAGenerator_AndWritesNumbersAsText()
        {
            var printed = Write(
                "rows = ([name, count] for name, count in [('Test 1', 3), ('Test 2', 0.5)])\n" +
                "print(csvexport.write_rows(path, ['Test', 'Total'], rows))");

            Assert.Equal("2", printed);
            var text = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(_path));
            Assert.Equal("﻿Test,Total\r\nTest 1,3\r\nTest 2,0.5\r\n", text);
        }
    }
}
