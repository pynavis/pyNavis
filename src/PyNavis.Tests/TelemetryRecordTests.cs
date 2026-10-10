using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;
using PyNavis.Runtime.Telemetry;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// One telemetry record is one JSON line: every field under its fixed name, in a
    /// fixed order, so a firm can read the files with any tool and diff two of them.
    /// </summary>
    public class TelemetryRecordTests
    {
        private static TelemetryRecord Sample() => new TelemetryRecord
        {
            Time = new DateTimeOffset(2026, 10, 10, 14, 5, 6, 789, TimeSpan.FromHours(5.5)),
            Session = "6f1c2a8e-0000-4000-8000-000000000001",
            User = "amol",
            Machine = "WS-042",
            Navis = "2026",
            PyNavis = "1.3.0",
            Doc = @"\server\projects\Tower\Tower_Coord.nwd",
            Kind = "click",
            Key = "pyNavis.tab/Clash.panel/Clear_Clash.pushbutton",
            Title = "Clear Clash",
            Extension = "pyNavis",
            Engine = "ironpython",
            Ms = 1234,
            Result = "ok",
        };

        private static Dictionary<string, object> Parse(string line) =>
            new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(line);

        [Fact]
        public void ToJsonLine_WritesEveryField_UnderItsName_InAFixedOrder()
        {
            var line = Sample().ToJsonLine();

            Assert.DoesNotContain("\n", line);
            var data = Parse(line);
            Assert.Equal(new[]
            {
                "schema", "time", "session", "user", "machine", "navis", "pynavis", "doc", "kind",
                "key", "title", "extension", "engine", "ms", "result", "error",
            }, data.Keys.ToArray());
            Assert.Equal(1, data["schema"]);
            Assert.Equal("2026-10-10T14:05:06.789+05:30", data["time"]);
            Assert.Equal("6f1c2a8e-0000-4000-8000-000000000001", data["session"]);
            Assert.Equal("amol", data["user"]);
            Assert.Equal("WS-042", data["machine"]);
            Assert.Equal("2026", data["navis"]);
            Assert.Equal("1.3.0", data["pynavis"]);
            Assert.Equal("click", data["kind"]);
            Assert.Equal("pyNavis.tab/Clash.panel/Clear_Clash.pushbutton", data["key"]);
            Assert.Equal("Clear Clash", data["title"]);
            Assert.Equal("pyNavis", data["extension"]);
            Assert.Equal("ironpython", data["engine"]);
            Assert.Equal(1234L, Convert.ToInt64(data["ms"]));
            Assert.Equal("ok", data["result"]);
            Assert.Equal("", data["error"]);
        }

        [Fact]
        public void ToJsonLine_WritesMissingTextAsEmpty_NeverNull()
        {
            var data = Parse(new TelemetryRecord { Time = DateTimeOffset.Now }.ToJsonLine());

            foreach (var name in new[] { "session", "user", "machine", "navis", "pynavis", "doc", "kind",
                                         "key", "title", "extension", "engine", "error" })
                Assert.Equal("", data[name]);
            Assert.Equal("ok", data["result"]);
        }

        [Theory]
        [InlineData(@"\server\projects\Tower\Tower_Coord.nwd", "Tower_Coord.nwd")]
        [InlineData(@"C:\Models\Site.nwf", "Site.nwf")]
        [InlineData("D:/models/forward/Plant.nwc", "Plant.nwc")]
        [InlineData("Loose.nwd", "Loose.nwd")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void Doc_IsTheFileNameOnly_NeverTheFolder(string doc, string expected)
        {
            var record = Sample();
            record.Doc = doc;

            Assert.Equal(expected, Parse(record.ToJsonLine())["doc"]);
        }

        [Fact]
        public void Error_IsTrimmedToItsFirstLine()
        {
            var record = Sample();
            record.Result = "error";
            record.Error = "\r\n  ValueError: the clash test has no results  \r\n  File \"script.py\", line 3\n    boom()";

            var data = Parse(record.ToJsonLine());

            Assert.Equal("error", data["result"]);
            Assert.Equal("ValueError: the clash test has no results", data["error"]);
        }

        [Fact]
        public void Error_IsEmpty_WhenTheRunWorked()
        {
            var record = Sample();
            record.Error = "left over from somewhere";

            Assert.Equal("", Parse(record.ToJsonLine())["error"]);
        }

        [Fact]
        public void FirstLine_CopesWithNullBlankAndLongText()
        {
            Assert.Equal("", TelemetryRecord.FirstLine(null));
            Assert.Equal("", TelemetryRecord.FirstLine(" \r\n \n"));
            Assert.Equal("one", TelemetryRecord.FirstLine("one\ntwo"));
            Assert.Equal("one", TelemetryRecord.FirstLine("one\r\ntwo"));
            Assert.Equal(TelemetryRecord.MaxErrorLength,
                TelemetryRecord.FirstLine(new string('x', 5000)).Length);
        }

        private const string Traceback =
            "Traceback (most recent call last):\r\n"
            + "  File \"C:\\Tools\\Clear_Clash.pushbutton\\script.py\", line 12, in <module>\r\n"
            + "    groups = plan(results)\r\n"
            + "ValueError: the clash test has no results\r\n\r\n";

        [Fact]
        public void ErrorSummary_OfATraceback_IsItsLastLine_TheOneThatNamesTheError()
        {
            // A traceback's first line only says a traceback follows; the last one says
            // what went wrong, and it does not carry the script's path.
            Assert.Equal("ValueError: the clash test has no results", TelemetryRecord.ErrorSummary(Traceback));
        }

        [Fact]
        public void ErrorSummary_OfAnythingElse_IsItsFirstLine()
        {
            Assert.Equal("Unknown engine 'lua'.", TelemetryRecord.ErrorSummary("\r\n  Unknown engine 'lua'.  \nat Some.Frame()"));
            Assert.Equal("Script exited with code 3.", TelemetryRecord.ErrorSummary("Script exited with code 3."));
        }

        [Fact]
        public void ErrorSummary_CopesWithNullBlankAndLongText()
        {
            Assert.Equal("", TelemetryRecord.ErrorSummary(null));
            Assert.Equal("", TelemetryRecord.ErrorSummary(" \r\n \n"));
            Assert.Equal(TelemetryRecord.MaxErrorLength,
                TelemetryRecord.ErrorSummary("Traceback (most recent call last):\n" + new string('x', 5000)).Length);
            Assert.Equal(TelemetryRecord.MaxErrorLength, TelemetryRecord.ErrorSummary(new string('x', 5000)).Length);
        }

        [Fact]
        public void Error_OfATraceback_IsWrittenAsItsLastLine()
        {
            var record = Sample();
            record.Result = "error";
            record.Error = Traceback;

            Assert.Equal("ValueError: the clash test has no results", Parse(record.ToJsonLine())["error"]);
        }

        [Fact]
        public void Results_RoundTrip_AndAreOmittedWhenThereAreNone()
        {
            var record = Sample();
            record.Results = new Dictionary<string, object>
            {
                ["clashes"] = 42,
                ["big"] = 12345678901L,
                ["ratio"] = 0.25,
                ["level"] = "Level 1",
                ["grouped"] = true,
                ["nothing"] = null,
                ["names"] = new List<object> { "Duct", 3 },
                ["by_test"] = new Dictionary<string, object> { ["Ducts vs Beams"] = 7 },
            };

            var results = Assert.IsType<Dictionary<string, object>>(Parse(record.ToJsonLine())["results"]);

            Assert.Equal(42, results["clashes"]);
            Assert.Equal(12345678901L, results["big"]);
            Assert.Equal(0.25, Convert.ToDouble(results["ratio"]));
            Assert.Equal("Level 1", results["level"]);
            Assert.Equal(true, results["grouped"]);
            Assert.Null(results["nothing"]);
            Assert.Equal(new object[] { "Duct", 3 }, Assert.IsType<ArrayList>(results["names"]).ToArray());
            Assert.Equal(7, Assert.IsType<Dictionary<string, object>>(results["by_test"])["Ducts vs Beams"]);

            record.Results = new Dictionary<string, object>();
            Assert.DoesNotContain("results", record.ToJsonLine());
            record.Results = null;
            Assert.DoesNotContain("results", record.ToJsonLine());
        }

        [Fact]
        public void Results_WriteAnythingAScriptHandsOver_AsPlainJson()
        {
            var record = Sample();
            record.Results = new Dictionary<string, object>
            {
                ["when"] = new DateTime(2026, 10, 10, 9, 30, 0),
                ["nan"] = double.NaN,
                ["odd"] = new Uri("https://example.test/a"),
                ["keys"] = new Hashtable { [1] = "one" },
            };

            var results = Assert.IsType<Dictionary<string, object>>(Parse(record.ToJsonLine())["results"]);

            Assert.Equal("2026-10-10T09:30:00.000", results["when"]);
            Assert.Null(results["nan"]);
            Assert.Equal("https://example.test/a", results["odd"]);
            Assert.Equal("one", Assert.IsType<Dictionary<string, object>>(results["keys"])["1"]);
        }
    }
}
