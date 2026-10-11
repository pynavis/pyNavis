using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Script.Serialization;
using PyNavis.Runtime;
using PyNavis.Runtime.Telemetry;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// Where records go: a file per machine, user and day in a shared folder, or a POST
    /// of a JSON array to the firm's endpoint; and the spool that keeps what could not
    /// be sent. All against a temp folder and a fake HTTP handler.
    /// </summary>
    public class TelemetrySinkTests : IDisposable
    {
        private readonly string _dir;
        private DateTime _now = new DateTime(2026, 10, 10, 14, 5, 0);

        public TelemetrySinkTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "pynavis_tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private static string Line(int n) => "{\"n\":" + n + "}";

        // ---- folder ----------------------------------------------------------------

        [Fact]
        public void FolderSink_WritesOneFilePerMachineUserAndDay()
        {
            var folder = Path.Combine(_dir, "share", "pynavis");             // made on first send
            var sink = new FolderSink(folder, "amol", "WS-042", () => _now);

            sink.Send(new[] { Line(1) });

            var expected = Path.Combine(folder, "WS-042-amol-2026-10-10.jsonl");
            Assert.Equal(expected, sink.CurrentFile);
            Assert.True(File.Exists(expected));
            Assert.Equal("folder", sink.Name);
            Assert.Equal(folder, sink.Target);

            _now = _now.AddDays(1);
            sink.Send(new[] { Line(2) });
            Assert.True(File.Exists(Path.Combine(folder, "WS-042-amol-2026-10-11.jsonl")));
        }

        [Fact]
        public void FolderSink_Appends_Utf8WithoutBom_WithNewlineEnds()
        {
            var sink = new FolderSink(_dir, "amol", "WS-042", () => _now);

            sink.Send(new[] { Line(1) });
            sink.Send(new[] { Line(2), "{\"t\":\"Ébauche\"}" });

            var bytes = File.ReadAllBytes(sink.CurrentFile);
            Assert.Equal((byte)'{', bytes[0]);                                 // no BOM
            Assert.Equal("{\"n\":1}\n{\"n\":2}\n{\"t\":\"Ébauche\"}\n", Encoding.UTF8.GetString(bytes));
        }

        [Fact]
        public void FolderSink_KeepsWriting_WhileSomeoneReadsTheFile()
        {
            var sink = new FolderSink(_dir, "amol", "WS-042", () => _now);
            sink.Send(new[] { Line(1) });

            using (new FileStream(sink.CurrentFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                sink.Send(new[] { Line(2) });

            Assert.Equal(2, File.ReadAllLines(sink.CurrentFile).Length);
        }

        [Fact]
        public void FolderSink_WritesAlone_SoTwoWindowsNeverInterleaveTheirLines()
        {
            // Two Navisworks windows of one user append to the same day file. Each send
            // waits briefly for the other's write to finish, then throws, and the service
            // keeps the lines to retry.
            var sink = new FolderSink(_dir, "amol", "WS-042", () => _now);
            sink.Send(new[] { Line(1) });

            using (new FileStream(sink.CurrentFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                Assert.ThrowsAny<IOException>(() => sink.Send(new[] { Line(2) }));

            sink.Send(new[] { Line(3) });
            Assert.Equal(new[] { Line(1), Line(3) }, File.ReadAllLines(sink.CurrentFile));
        }

        [Fact]
        public void FolderSink_MakesUserAndMachineSafeForAFileName()
        {
            var sink = new FolderSink(_dir, @"corp\a:b*c", "  ", () => _now);

            Assert.Equal(Path.Combine(_dir, "unknown-corp_a_b_c-2026-10-10.jsonl"), sink.CurrentFile);
        }

        [Fact]
        public void FolderSink_Throws_WhenTheFolderCannotBeWritten()
        {
            var blocker = Path.Combine(_dir, "not-a-folder");
            File.WriteAllText(blocker, "a file where the folder should be");
            var sink = new FolderSink(blocker, "amol", "WS-042", () => _now);

            Assert.ThrowsAny<IOException>(() => sink.Send(new[] { Line(1) }));
        }

        // ---- http ------------------------------------------------------------------

        [Fact]
        public void HttpSink_PostsTheLinesAsOneJsonArray()
        {
            var handler = new FakeHttpHandler(HttpStatusCode.OK, "", "text/plain");
            var sink = new HttpSink("https://telemetry.example.test/pynavis", handler, TimeSpan.FromSeconds(5));

            sink.Send(new[] { "{\"title\":\"Clear Clash\"}", "{\"title\":\"Set Gap\"}" });

            var request = Assert.Single(handler.Requests);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://telemetry.example.test/pynavis", request.RequestUri.ToString());
            Assert.Equal("application/json", request.Content.Headers.ContentType.MediaType);
            Assert.Equal("pyNavis/" + PyNavisVersion.Product, request.Headers.UserAgent.ToString());
            var body = Assert.IsType<object[]>(new JavaScriptSerializer().DeserializeObject(handler.LastBody));
            Assert.Equal(2, body.Length);
            Assert.Equal("Set Gap", ((Dictionary<string, object>)body[1])["title"]);
            Assert.Equal("http", sink.Name);
            Assert.Equal("https://telemetry.example.test/pynavis", sink.Target);
        }

        [Theory]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.ServiceUnavailable)]
        [InlineData(HttpStatusCode.RequestTimeout)]                 // 408: the server asks for it again
        [InlineData((HttpStatusCode)429)]                           // too many requests: later
        [InlineData(HttpStatusCode.ProxyAuthenticationRequired)]    // 407: the proxy said no, not the server
        public void HttpSink_TreatsAFailureThatMayPass_AsOneToRetry(HttpStatusCode status)
        {
            var handler = new FakeHttpHandler(status, "nope", "text/plain");
            var sink = new HttpSink("https://telemetry.example.test/pynavis", handler, TimeSpan.FromSeconds(5));

            var ex = Assert.Throws<HttpRequestException>(() => sink.Send(new[] { Line(1) }));
            Assert.Contains(((int)status).ToString(), ex.Message);
        }

        [Theory]
        [InlineData(HttpStatusCode.BadRequest)]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.NotFound)]
        [InlineData(HttpStatusCode.RequestEntityTooLarge)]
        [InlineData((HttpStatusCode)422)]
        public void HttpSink_TreatsAClientError_AsARefusal(HttpStatusCode status)
        {
            // The server read the batch and will not take it: sending it again cannot help.
            var handler = new FakeHttpHandler(status, "nope", "text/plain");
            var sink = new HttpSink("https://telemetry.example.test/pynavis", handler, TimeSpan.FromSeconds(5));

            var ex = Assert.Throws<TelemetryRejectedException>(() => sink.Send(new[] { Line(1) }));
            Assert.Equal((int)status, ex.Status);
            Assert.StartsWith("the server answered " + (int)status, ex.Message);
        }

        [Theory]
        [InlineData("ftp://example.test/x")]
        [InlineData("not a url")]
        [InlineData("/relative/path")]
        public void HttpSink_RefusesWhatIsNotAnHttpAddress(string url)
        {
            Assert.Throws<ArgumentException>(
                () => new HttpSink(url, new FakeHttpHandler(HttpStatusCode.OK, ""), TimeSpan.FromSeconds(5)));
        }

        // ---- spool -----------------------------------------------------------------

        [Fact]
        public void Spool_PeekAll_ReturnsTheLinesInOrder_AndLeavesThemInTheFile()
        {
            var spool = new TelemetrySpool(Path.Combine(_dir, "spool", "http.jsonl"));
            Assert.Empty(spool.PeekAll());                                     // nothing yet, no file needed

            spool.Append(new[] { Line(1), Line(2) });
            spool.Append(new[] { Line(3) });
            Assert.Equal(3, spool.Count());

            Assert.Equal(new[] { Line(1), Line(2), Line(3) }, spool.PeekAll());
            Assert.Equal(new[] { Line(1), Line(2), Line(3) }, spool.PeekAll());
            Assert.Equal(3, spool.Count());
        }

        [Fact]
        public void Spool_Discard_TakesOutTheLinesSent_AndKeepsEveryOther()
        {
            var spool = new TelemetrySpool(Path.Combine(_dir, "http.jsonl"));
            spool.Append(new[] { Line(1), Line(2), Line(3) });
            var sent = spool.PeekAll().Take(2).ToList();
            new TelemetrySpool(spool.Path).Append(new[] { Line(4) });          // the other window, meanwhile

            Assert.Equal(2, spool.Discard(sent));
            Assert.Equal(new[] { Line(3), Line(4) }, spool.PeekAll());
            Assert.Equal(2, spool.Count());

            // The other window sent them too and took them out first: nothing else goes.
            Assert.Equal(0, spool.Discard(sent));
            Assert.Equal(new[] { Line(3), Line(4) }, File.ReadAllLines(spool.Path));
            Assert.Equal(0, new TelemetrySpool(Path.Combine(_dir, "none.jsonl")).Discard(sent));
        }

        [Fact]
        public void Spool_SkipsATornLine_LeftByACrashMidWrite()
        {
            var path = Path.Combine(_dir, "folder.jsonl");
            File.WriteAllText(path, Line(1) + "\n{\"n\":2, \"tit");
            var spool = new TelemetrySpool(path);

            Assert.Equal(new[] { Line(1) }, spool.PeekAll());
        }

        [Fact]
        public void Spool_AppendsAfterATornLine_OnALineOfItsOwn()
        {
            // Otherwise the torn end and the new record would read as one line that looks
            // whole but is not JSON, and a server would refuse the batch that carries it.
            var path = Path.Combine(_dir, "http.jsonl");
            File.WriteAllText(path, Line(1) + "\n{\"n\":2, \"tit");
            var spool = new TelemetrySpool(path);

            spool.Append(new[] { Line(3) });

            Assert.Equal(new[] { Line(1), Line(3) }, spool.PeekAll());
        }

        // The rule: when an append takes the file past maxBytes, the oldest lines go
        // until what is left fits in three quarters of maxBytes, so a long outage trims
        // now and then instead of on every flush.
        [Fact]
        public void Spool_OverItsCap_DropsTheOldestLines_DownToThreeQuarters()
        {
            static string Padded(int n) => "{\"n\":\"" + n.ToString("D2") + new string('x', 39) + "\"}"; // 49 chars + \n = 50 bytes
            var spool = new TelemetrySpool(Path.Combine(_dir, "http.jsonl"), maxBytes: 1000);

            Assert.Equal(0, spool.Append(Enumerable.Range(1, 10).Select(Padded)));   // 500 bytes
            Assert.Equal(0, spool.Append(Enumerable.Range(11, 10).Select(Padded)));  // 1000: at the cap, not over
            var dropped = spool.Append(Enumerable.Range(21, 10).Select(Padded));     // 1500: over

            Assert.Equal(15, dropped);
            Assert.True(new FileInfo(spool.Path).Length <= 750);
            Assert.Equal(Enumerable.Range(16, 15).Select(Padded), spool.PeekAll());
            Assert.Equal(15, spool.Count());
        }
    }
}
