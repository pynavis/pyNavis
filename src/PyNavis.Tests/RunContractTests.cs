using System;
using System.IO;
using PyNavis.Shared;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The request/response files the CLI and the runner add-in hand each other. Both
    /// sides compile the same source, so a field that does not survive a save and a load
    /// here is a field the other side never sees.
    /// </summary>
    public class RunContractTests : IDisposable
    {
        private readonly string _dir;

        public RunContractTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "pynavis_tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private string PathFor(string name) => Path.Combine(_dir, name);

        [Fact]
        public void RunRequest_SaveThenLoad_RoundTripsEveryField()
        {
            var original = new RunRequest
            {
                Script = @"C:\scripts\count.py",
                Model = @"C:\models\a.nwd",
                Models = new[] { @"C:\models\a.nwd", @"C:\models\b.nwd" },
                Args = new[] { "--level", "2", "naïve" },
                RunDir = @"C:\runs\20261004-101500",
                ResponsePath = @"C:\runs\20261004-101500\response-1.json",
                TranscriptPath = @"C:\runs\20261004-101500\transcript-1.txt",
            };
            var path = PathFor("request-1.json");

            original.Save(path);
            var loaded = RunRequest.Load(path);

            Assert.Equal(original.Script, loaded.Script);
            Assert.Equal(original.Model, loaded.Model);
            Assert.Equal(original.Models, loaded.Models);
            Assert.Equal(original.Args, loaded.Args);
            Assert.Equal(original.RunDir, loaded.RunDir);
            Assert.Equal(original.ResponsePath, loaded.ResponsePath);
            Assert.Equal(original.TranscriptPath, loaded.TranscriptPath);
        }

        [Fact]
        public void RunRequest_RoundTrips_EmptyArrays_AndNullModel()
        {
            var original = new RunRequest
            {
                Script = @"C:\scripts\report.py",
                Model = null,
                Models = new string[0],
                Args = new string[0],
                RunDir = @"C:\runs\x",
                ResponsePath = @"C:\runs\x\response-0.json",
                TranscriptPath = @"C:\runs\x\transcript-0.txt",
            };
            var path = PathFor("request-0.json");

            original.Save(path);
            var loaded = RunRequest.Load(path);

            Assert.Null(loaded.Model);
            Assert.NotNull(loaded.Models);
            Assert.Empty(loaded.Models);
            Assert.NotNull(loaded.Args);
            Assert.Empty(loaded.Args);
        }

        [Fact]
        public void RunRequest_UsesLowerCamelJsonKeys()
        {
            var path = PathFor("request.json");
            new RunRequest
            {
                Script = "s", Model = "m", Models = new[] { "m" }, Args = new string[0],
                RunDir = "d", ResponsePath = "r", TranscriptPath = "t",
            }.Save(path);

            var text = File.ReadAllText(path);
            foreach (var key in new[] { "script", "model", "models", "args", "runDir", "responsePath", "transcriptPath" })
                Assert.Contains("\"" + key + "\":", text);
        }

        [Fact]
        public void RunResponse_SaveThenLoad_RoundTripsEveryField()
        {
            var original = new RunResponse
            {
                Succeeded = false,
                Error = "Traceback (most recent call last):\n  File \"x.py\", line 1\nValueError: bad",
                DurationMs = 12345678901L,
                ResultJson = "{\"count\": 3, \"names\": [\"a\", \"b\"]}",
                HostPid = 31337,
            };
            var path = PathFor("response-1.json");

            original.Save(path);
            var loaded = RunResponse.Load(path);

            Assert.Equal(original.Succeeded, loaded.Succeeded);
            Assert.Equal(original.Error, loaded.Error);
            Assert.Equal(original.DurationMs, loaded.DurationMs);
            Assert.Equal(original.ResultJson, loaded.ResultJson);
            Assert.Equal(31337, loaded.HostPid);
        }

        [Fact]
        public void RunResponse_WithoutAHostPid_LoadsAsZero()
        {
            var path = PathFor("response-old.json");
            File.WriteAllText(path, "{\"succeeded\": true, \"error\": null, \"durationMs\": 5, \"resultJson\": null}");

            Assert.Equal(0, RunResponse.Load(path).HostPid);
        }

        [Fact]
        public void RunResponse_RoundTrips_NullErrorAndResult()
        {
            var original = new RunResponse { Succeeded = true, Error = null, DurationMs = 0, ResultJson = null };
            var path = PathFor("response-2.json");

            original.Save(path);
            var loaded = RunResponse.Load(path);

            Assert.True(loaded.Succeeded);
            Assert.Null(loaded.Error);
            Assert.Equal(0, loaded.DurationMs);
            Assert.Null(loaded.ResultJson);
        }

        [Fact]
        public void RunResponse_UsesLowerCamelJsonKeys()
        {
            var path = PathFor("response.json");
            new RunResponse { Succeeded = true, Error = "e", DurationMs = 1, ResultJson = "1" }.Save(path);

            var text = File.ReadAllText(path);
            foreach (var key in new[] { "succeeded", "error", "durationMs", "resultJson", "hostPid" })
                Assert.Contains("\"" + key + "\":", text);
        }

        [Fact]
        public void Load_MissingFile_Throws()
        {
            var path = PathFor("nope.json");
            Assert.ThrowsAny<Exception>(() => RunRequest.Load(path));
            Assert.ThrowsAny<Exception>(() => RunResponse.Load(path));
        }

        [Theory]
        [InlineData("{ not json")]
        [InlineData("")]
        [InlineData("[1, 2]")]
        [InlineData("null")]
        public void Load_InvalidJson_Throws(string text)
        {
            var path = PathFor("bad.json");
            File.WriteAllText(path, text);
            Assert.ThrowsAny<Exception>(() => RunRequest.Load(path));
            Assert.ThrowsAny<Exception>(() => RunResponse.Load(path));
        }
    }
}
