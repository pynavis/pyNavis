using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using PyNavis.Cli;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>The results.json that <c>pynavis run</c> leaves in the run folder.</summary>
    public class RunSummaryTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "pynavis_tests", Guid.NewGuid().ToString("N"));

        public RunSummaryTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        [Fact]
        public void Save_EmbedsTheResultAsAnObject_AndNullWhenAbsent()
        {
            var summary = new RunSummary
            {
                Script = @"C:\scripts\audit.py",
                Year = "2026",
                StartedAt = "2026-10-04T09:30:00+02:00",
                FinishedAt = "2026-10-04T09:31:00+02:00",
            };
            summary.Models.Add(new ModelOutcome { Model = @"C:\m\a.nwd", Status = "ok", DurationMs = 1200, ResultJson = "{\"a\":1}" });
            summary.Models.Add(new ModelOutcome { Model = @"C:\m\b.nwd", Status = "failed", DurationMs = 5, Error = "Boom" });
            var path = Path.Combine(_dir, "results.json");

            summary.Save(path);

            var json = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path));
            Assert.Equal(@"C:\scripts\audit.py", json["script"]);
            Assert.Equal("2026", json["year"]);
            Assert.Equal("2026-10-04T09:30:00+02:00", json["startedAt"]);
            Assert.Equal("2026-10-04T09:31:00+02:00", json["finishedAt"]);
            Assert.Equal(false, json["cancelled"]);
            var models = (object[])json["models"];
            var first = (Dictionary<string, object>)models[0];
            Assert.Equal(@"C:\m\a.nwd", first["model"]);
            Assert.Equal("ok", first["status"]);
            Assert.Equal(1200, Convert.ToInt32(first["durationMs"]));
            Assert.Null(first["error"]);
            var result = Assert.IsType<Dictionary<string, object>>(first["result"]);
            Assert.Equal(1, result["a"]);
            var second = (Dictionary<string, object>)models[1];
            Assert.Equal("failed", second["status"]);
            Assert.Equal("Boom", second["error"]);
            Assert.True(second.ContainsKey("result"));
            Assert.Null(second["result"]);
        }

        [Fact]
        public void Save_WritesTheSkippedAndNotRunStatuses_WithTheirMessages()
        {
            var summary = new RunSummary { Script = "audit.py", Year = "2026" };
            summary.Models.Add(new ModelOutcome { Model = @"C:\m\a.ifc", Status = RunLoop.SaveSkipped, Error = "Only .nwd and .nwf files are saved back." });
            summary.Models.Add(new ModelOutcome { Model = @"C:\m\b.nwd", Status = RunLoop.NotRun, Error = "Not run: the run ended after an earlier model." });
            var path = Path.Combine(_dir, "results.json");

            summary.Save(path);

            var json = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path));
            var models = (object[])json["models"];
            Assert.Equal("save-skipped", ((Dictionary<string, object>)models[0])["status"]);
            Assert.Equal("Only .nwd and .nwf files are saved back.", ((Dictionary<string, object>)models[0])["error"]);
            Assert.Equal("not-run", ((Dictionary<string, object>)models[1])["status"]);
            Assert.Equal("Not run: the run ended after an earlier model.", ((Dictionary<string, object>)models[1])["error"]);
        }

        [Fact]
        public void Save_WritesCancelled_WhenCtrlCEndedTheRun()
        {
            var summary = new RunSummary { Script = "audit.py", Year = "2026", Cancelled = true };
            var path = Path.Combine(_dir, "results.json");

            summary.Save(path);

            var json = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path));
            Assert.Equal(true, json["cancelled"]);
        }

        [Fact]
        public void Save_WritesAResultAsDeepAsTheReaderAccepts()
        {
            // 99 lists round a number: 100 levels, the deepest result the reader takes in.
            // In results.json it sits three levels further down (the root, the models list,
            // the model's entry), which once overran the writer's limit, so Save threw and
            // the whole file was lost.
            var deep = new string('[', 99) + "7" + new string(']', 99);
            var summary = new RunSummary { Script = "audit.py", Year = "2026" };
            summary.Models.Add(new ModelOutcome { Model = @"C:\m\a.nwd", Status = "ok", ResultJson = deep });
            var path = Path.Combine(_dir, "results.json");

            summary.Save(path);

            var json = (Dictionary<string, object>)new JavaScriptSerializer { RecursionLimit = 200 }
                .DeserializeObject(File.ReadAllText(path));
            var result = ((Dictionary<string, object>)((object[])json["models"])[0])["result"];
            var depth = 0;
            while (result is object[] list)
            {
                depth++;
                result = Assert.Single(list);
            }
            Assert.Equal(99, depth);
            Assert.Equal(7, result);
        }

        [Fact]
        public void Save_KeepsAResultTooDeepForTheReader_AsItsText()
        {
            var deeper = new string('[', 150) + new string(']', 150);
            var summary = new RunSummary { Script = "audit.py", Year = "2026" };
            summary.Models.Add(new ModelOutcome { Model = @"C:\m\a.nwd", Status = "ok", ResultJson = deeper });
            var path = Path.Combine(_dir, "results.json");

            summary.Save(path);

            var json = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path));
            Assert.Equal(deeper, ((Dictionary<string, object>)((object[])json["models"])[0])["result"]);
        }
    }
}
