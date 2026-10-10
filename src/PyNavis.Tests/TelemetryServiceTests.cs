using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using PyNavis.Runtime.Telemetry;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The queue and the service around it: what gets recorded, what a flush sends,
    /// what happens when a sink fails, and how quietly it says so. A fake clock and
    /// fake sinks; the spool is a real file in a temp folder.
    /// </summary>
    public class TelemetryServiceTests : IDisposable
    {
        private readonly string _dir;
        private readonly List<string> _logged = new List<string>();
        private DateTime _now = new DateTime(2026, 10, 10, 14, 5, 0);

        public TelemetryServiceTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "pynavis_tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private sealed class FakeSink : ITelemetrySink
        {
            private readonly object _gate = new object();
            private readonly List<string> _lines = new List<string>();

            public FakeSink(string name = "fake") { Name = name; }

            public string Name { get; }
            public string Target => "the " + Name + " target";
            public volatile bool Fail;
            public int Attempts;

            public List<string> Lines { get { lock (_gate) return _lines.ToList(); } }
            public List<string> Titles => Lines.Select(TitleOf).ToList();

            public void Send(IReadOnlyList<string> lines)
            {
                Interlocked.Increment(ref Attempts);
                if (Fail) throw new IOException("the share is offline");
                lock (_gate) _lines.AddRange(lines);
            }
        }

        private static string TitleOf(string line) =>
            (string)new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(line)["title"];

        private static TelemetryRecord Click(string title, string kind = "click") => new TelemetryRecord
        {
            Time = DateTimeOffset.Now, Kind = kind, Title = title, Key = "x.tab/y.panel/" + title + ".pushbutton",
        };

        private static TelemetryConfig On(bool includeHooks = false) =>
            new TelemetryConfig { Enabled = true, IncludeHooks = includeHooks };

        private TelemetryService Service(TelemetryConfig config, params ITelemetrySink[] sinks) =>
            new TelemetryService(config, sinks, Path.Combine(_dir, "spool"), () => _now, _logged.Add);

        private int Spooled(string name) => new TelemetrySpool(Path.Combine(_dir, "spool", name + ".jsonl")).Count();

        // ---- the queue -------------------------------------------------------------

        [Fact]
        public void Queue_OverCapacity_DropsTheOldest_AndCountsThem()
        {
            var queue = new TelemetryQueue(3);
            foreach (var n in Enumerable.Range(1, 5)) queue.Enqueue(Click("T" + n));

            Assert.Equal(3, queue.Count);
            Assert.Equal(2, queue.Dropped);
            Assert.Equal(new[] { "T3", "T4" }, queue.Drain(2).Select(r => r.Title));
            Assert.Equal(new[] { "T5" }, queue.Drain(10).Select(r => r.Title));
            Assert.Empty(queue.Drain(10));
            Assert.Equal(TelemetryQueue.DefaultCapacity, new TelemetryQueue().Capacity);
            Assert.Equal(1000, TelemetryQueue.DefaultCapacity);
        }

        // ---- what gets recorded ----------------------------------------------------

        [Fact]
        public void Disabled_RecordsNothing_AndSaysItIsOff()
        {
            var sink = new FakeSink();
            var service = Service(new TelemetryConfig { Enabled = false, Folder = @"C:\t" }, sink);

            service.Record(Click("Clear Clash"));
            var result = service.Flush();

            Assert.False(service.IsActive);
            Assert.Equal(0, result.Sent);
            Assert.Equal(0, sink.Attempts);
            Assert.Equal("Telemetry is off.", service.Status());
        }

        [Fact]
        public void Disabled_ByTheFirm_SaysSo()
        {
            var service = Service(new TelemetryConfig { Enabled = false, Managed = true }, new FakeSink());

            Assert.Equal("Telemetry is off. Your firm manages this setting.", service.Status());
        }

        [Fact]
        public void Enabled_WithNoSinks_RecordsNothing()
        {
            var service = Service(On());

            service.Record(Click("Clear Clash"));

            Assert.False(service.IsActive);
            Assert.Equal(0, service.Flush().Sent);
        }

        [Fact]
        public void HookRuns_AreLeftOut_UnlessIncludeHooksIsOn()
        {
            var sink = new FakeSink();
            var service = Service(On(), sink);
            service.Record(Click("Track viewpoints", kind: "hook"));
            service.Record(Click("Clear Clash"));
            service.Flush();
            Assert.Equal(new[] { "Clear Clash" }, sink.Titles);

            var withHooks = new FakeSink();
            var both = Service(On(includeHooks: true), withHooks);
            both.Record(Click("Track viewpoints", kind: "hook"));
            both.Flush();
            Assert.Equal(new[] { "Track viewpoints" }, withHooks.Titles);
        }

        [Fact]
        public void Record_NeverThrows()
        {
            var service = Service(On(), new FakeSink());

            service.Record(null);
            service.Record(new TelemetryRecord { Kind = null });
        }

        // ---- flushing --------------------------------------------------------------

        [Fact]
        public void Flush_SendsEveryRecordAsOneJsonLine_InOrder()
        {
            var sink = new FakeSink();
            var service = Service(On(), sink);
            service.Record(Click("Clear Clash"));
            service.Record(Click("Set Gap"));

            var result = service.Flush();

            Assert.Equal(2, result.Sent);
            Assert.Equal(0, result.Spooled);
            Assert.Equal(0, result.Dropped);
            Assert.Equal(new[] { "Clear Clash", "Set Gap" }, sink.Titles);

            var again = service.Flush();                                        // nothing new: no call at all
            Assert.Equal(0, again.Sent);
            Assert.Equal(1, sink.Attempts);
        }

        [Fact]
        public void Flush_ReportsWhatTheFullQueueDropped()
        {
            var sink = new FakeSink();
            var service = Service(On(), sink);
            for (var n = 0; n < TelemetryQueue.DefaultCapacity + 5; n++) service.Record(Click("T" + n));

            var result = service.Flush();

            Assert.Equal(TelemetryQueue.DefaultCapacity, result.Sent);
            Assert.Equal(5, result.Dropped);
            Assert.Equal("T5", sink.Titles.First());
            Assert.Equal(0, service.Flush().Dropped);                           // reported once
        }

        [Fact]
        public void Flush_SendsBigBacklogsInBatches()
        {
            var sink = new FakeSink();
            var service = Service(On(), sink);
            for (var n = 0; n < TelemetryService.BatchSize + 1; n++) service.Record(Click("T" + n));

            Assert.Equal(TelemetryService.BatchSize + 1, service.Flush().Sent);
            Assert.Equal(2, sink.Attempts);
        }

        [Fact]
        public void AFailedSend_IsSpooled_AndTheNextFlushSendsIt_BeforeNewerRecords()
        {
            var sink = new FakeSink { Fail = true };
            var service = Service(On(), sink);
            service.Record(Click("Clear Clash"));

            var failed = service.Flush();

            Assert.Equal(0, failed.Sent);
            Assert.Equal(1, failed.Spooled);
            Assert.Equal(1, Spooled("fake"));

            sink.Fail = false;
            service.Record(Click("Set Gap"));
            var retried = service.Flush();

            Assert.Equal(2, retried.Sent);
            Assert.Equal(new[] { "Clear Clash", "Set Gap" }, sink.Titles);
            Assert.Equal(0, Spooled("fake"));
        }

        [Fact]
        public void OnlyTheFirstFailure_IsLogged_UntilASendWorksAgain()
        {
            var sink = new FakeSink { Fail = true };
            var service = Service(On(), sink);

            for (var n = 0; n < 3; n++)
            {
                service.Record(Click("T" + n));
                service.Flush();
                _now = _now.AddMinutes(10);                                     // past any retry delay
            }
            var line = Assert.Single(_logged);
            Assert.Contains("the fake target", line);
            Assert.Contains("the share is offline", line);

            sink.Fail = false;
            service.Flush();
            sink.Fail = true;
            service.Record(Click("T9"));
            service.Flush();

            Assert.Equal(2, _logged.Count);
        }

        [Fact]
        public void AFailingSink_IsRetriedLessOften_TheLongerItFails()
        {
            var sink = new FakeSink { Fail = true };
            var service = Service(On(), sink);
            service.Record(Click("Clear Clash"));

            service.Flush();                                                    // first failure
            service.Flush();                                                    // retried at once
            Assert.Equal(2, sink.Attempts);

            service.Record(Click("Set Gap"));
            var waiting = service.Flush();                                      // second failure: wait 10 s
            Assert.Equal(2, sink.Attempts);
            Assert.Equal(1, waiting.Spooled);                                   // the new record is parked
            Assert.Equal(2, Spooled("fake"));

            _now = _now.AddSeconds(10);
            sink.Fail = false;
            Assert.Equal(2, service.Flush().Sent);
            Assert.Equal(new[] { "Clear Clash", "Set Gap" }, sink.Titles);

            Assert.Equal(TimeSpan.Zero, TelemetryService.RetryDelay(1));
            Assert.Equal(TimeSpan.FromSeconds(10), TelemetryService.RetryDelay(2));
            Assert.Equal(TimeSpan.FromSeconds(20), TelemetryService.RetryDelay(3));
            Assert.Equal(TimeSpan.FromMinutes(5), TelemetryService.RetryDelay(50));
        }

        [Fact]
        public void AFailingSink_NeitherHoldsBackNorRepeatsTheOtherOne()
        {
            var good = new FakeSink("folder");
            var bad = new FakeSink("http") { Fail = true };
            var service = Service(On(), good, bad);
            service.Record(Click("Clear Clash"));

            var first = service.Flush();
            Assert.Equal(1, first.Sent);
            Assert.Equal(1, first.Spooled);

            _now = _now.AddMinutes(10);
            service.Flush();                                                    // bad fails again
            bad.Fail = false;
            _now = _now.AddMinutes(10);
            service.Flush();

            Assert.Equal(new[] { "Clear Clash" }, good.Titles);                 // once, never resent
            Assert.Equal(new[] { "Clear Clash" }, bad.Titles);
        }

        [Fact]
        public void TheSpoolCap_DropsTheOldest_AndTheFlushSaysHowMany()
        {
            var sink = new FakeSink { Fail = true };
            var service = new TelemetryService(On(), new[] { sink }, Path.Combine(_dir, "spool"),
                () => _now, _logged.Add, spoolMaxBytes: 1000);
            for (var n = 0; n < 20; n++) service.Record(Click("Tool number " + n));

            var result = service.Flush();

            var kept = Spooled("fake");
            Assert.Equal(20, result.Spooled);
            Assert.True(kept > 0 && kept < 20);
            Assert.Equal(20 - kept, result.Dropped);
        }

        // ---- status ----------------------------------------------------------------

        [Fact]
        public void Status_SaysWhenItLastSent_WhatWaits_AndTheLastError()
        {
            var sink = new FakeSink();
            var service = Service(On(), sink);
            Assert.Equal("Nothing sent yet. 0 records waiting, 0 in the spool.", service.Status());

            service.Record(Click("Clear Clash"));
            Assert.Equal("Nothing sent yet. 1 record waiting, 0 in the spool.", service.Status());

            service.Flush();
            Assert.Equal("Last sent 10 Oct 2026 14:05:00. 0 records waiting, 0 in the spool.", service.Status());

            sink.Fail = true;
            service.Record(Click("Set Gap"));
            _now = _now.AddMinutes(1);
            service.Flush();
            Assert.Equal("Last sent 10 Oct 2026 14:05:00. 0 records waiting, 1 in the spool. "
                         + "The last send to the fake target failed: the share is offline.", service.Status());

            sink.Fail = false;
            service.Flush();
            Assert.DoesNotContain("failed", service.Status());
        }

        // ---- building from settings ------------------------------------------------

        [Fact]
        public void Create_BuildsASinkForEachPlaceNamed()
        {
            var both = TelemetryService.Create(new TelemetryConfig
            {
                Enabled = true, Folder = Path.Combine(_dir, "share"), Url = "https://telemetry.example.test/t",
            }, Path.Combine(_dir, "spool"), _logged.Add);

            Assert.True(both.IsActive);
            Assert.Equal(new[] { "folder", "http" }, both.Sinks.Select(s => s.Name));

            var badUrl = TelemetryService.Create(new TelemetryConfig
            {
                Enabled = true, Folder = Path.Combine(_dir, "share"), Url = "ftp://nope",
            }, Path.Combine(_dir, "spool"), _logged.Add);

            Assert.Equal(new[] { "folder" }, badUrl.Sinks.Select(s => s.Name));
            Assert.Contains("ftp://nope", Assert.Single(_logged));

            var off = TelemetryService.Create(new TelemetryConfig { Enabled = false, Folder = _dir },
                Path.Combine(_dir, "spool"), _logged.Add);
            Assert.False(off.IsActive);
            Assert.Empty(off.Sinks);
        }

        // ---- the worker ------------------------------------------------------------

        [Fact]
        public void TheWorker_FlushesOnItsOwn()
        {
            var sink = new FakeSink();
            var service = Service(On(), sink);
            service.Interval = TimeSpan.FromMilliseconds(20);

            service.Start();
            service.Record(Click("Clear Clash"));
            var clock = Stopwatch.StartNew();
            while (sink.Lines.Count == 0 && clock.Elapsed < TimeSpan.FromSeconds(10)) Thread.Sleep(10);
            service.Stop();

            Assert.Equal(new[] { "Clear Clash" }, sink.Titles);
        }

        [Fact]
        public void Stop_FlushesWhatIsLeft()
        {
            var sink = new FakeSink();
            var service = Service(On(), sink);
            service.Interval = TimeSpan.FromHours(1);

            service.Start();
            service.Record(Click("Clear Clash"));
            Assert.True(service.Stop());

            Assert.Equal(new[] { "Clear Clash" }, sink.Titles);
            Assert.True(service.Stop());                                        // twice is harmless
        }

        [Fact]
        public void Stop_GivesUp_AfterItsBoundedWait()
        {
            var service = Service(On(), new SlowSink());
            service.Record(Click("Clear Clash"));

            var clock = Stopwatch.StartNew();
            Assert.False(service.Stop());

            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(4));
        }

        private sealed class SlowSink : ITelemetrySink
        {
            public string Name => "slow";
            public string Target => "a slow server";
            public void Send(IReadOnlyList<string> lines) => Thread.Sleep(TimeSpan.FromSeconds(6));
        }
    }
}
