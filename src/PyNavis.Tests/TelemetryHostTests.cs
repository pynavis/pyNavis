using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using PyNavis.Runtime;
using PyNavis.Runtime.Bundles;
using PyNavis.Runtime.Engine;
using PyNavis.Runtime.Events;
using PyNavis.Runtime.Execution;
using PyNavis.Runtime.Telemetry;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>A sink that keeps what it is sent, for tests that read records back.</summary>
    internal sealed class CollectingSink : ITelemetrySink
    {
        private readonly object _gate = new object();
        private readonly List<string> _lines = new List<string>();

        public string Name => "collect";
        public string Target => "the test sink";

        public List<Dictionary<string, object>> Records
        {
            get
            {
                lock (_gate)
                    return _lines.Select(l => new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(l)).ToList();
            }
        }

        public void Send(IReadOnlyList<string> lines)
        {
            lock (_gate) _lines.AddRange(lines);
        }

        /// <summary>An active service sending only here, its spool under <paramref name="dir"/>.</summary>
        public TelemetryService Service(string dir, bool includeHooks = false) =>
            new TelemetryService(new TelemetryConfig { Enabled = true, IncludeHooks = includeHooks },
                new ITelemetrySink[] { this }, Path.Combine(dir, "spool"), () => DateTime.Now, _ => { });
    }

    /// <summary>
    /// The runtime's side of telemetry: the one service everything records into, the
    /// firm file it trusts, how a run becomes a record, and the door a script's
    /// pynavis.telemetry.note comes in through. TelemetryHost and PyNavisHost are
    /// process-wide, so these share RunExecutorTests' non-parallel collection.
    /// </summary>
    [Collection(RunnerContextCollection.Name)]
    public class TelemetryHostTests : IDisposable
    {
        private readonly string _dir;
        private readonly CollectingSink _sink = new CollectingSink();
        private readonly TelemetryService _previous;
        private readonly List<string> _logged = new List<string>();

        public TelemetryHostTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "pynavis_tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _previous = TelemetryHost.Current;
        }

        public void Dispose()
        {
            TelemetryHost.Install(_previous);
            while (PyNavisHost.Instance.EndTelemetry() != null) { }
            try { Directory.Delete(_dir, true); } catch { }
        }

        private void On(bool includeHooks = false) => TelemetryHost.Install(_sink.Service(_dir, includeHooks));

        private Dictionary<string, object> OnlyRecord()
        {
            TelemetryHost.Current.Flush();
            return Assert.Single(_sink.Records);
        }

        private static PushButtonModel Button() => new PushButtonModel
        {
            Title = "Clear Clash",
            BundleKey = "pyNavis.tab/Clash.panel/Clear_Clash.pushbutton",
            ExtensionName = "pyNavis",
            EngineId = "cpython",
            Directory = @"C:\ext\pyNavis.extension\pyNavis.tab\Clash.panel\Clear_Clash.pushbutton",
            ScriptPath = @"C:\ext\pyNavis.extension\pyNavis.tab\Clash.panel\Clear_Clash.pushbutton\script.py",
        };

        // ---- the service ------------------------------------------------------------

        [Fact]
        public void Current_IsNeverNull_AndRecordsNothing_UntilSomethingIsConfigured()
        {
            TelemetryHost.Install(null);

            Assert.NotNull(TelemetryHost.Current);
            Assert.False(TelemetryHost.Current.IsActive);
            Assert.False(TelemetryHost.Wants("click"));
            Assert.Null(TelemetryHost.ForButton(Button(), "click"));
            Assert.Equal("Telemetry is off.", TelemetryHost.Current.Status());
        }

        [Fact]
        public void Configure_StartsTheServiceForTheSettings_AndStopsTheOneItReplaces()
        {
            var share = Path.Combine(_dir, "share");
            var spool = Path.Combine(_dir, "spool");
            var user = new TelemetryConfig { Enabled = true, Folder = share };

            TelemetryHost.Configure(user, null, spool, _ => false);
            Assert.True(TelemetryHost.Current.IsActive);
            TelemetryHost.Finish(TelemetryHost.ForButton(Button(), "click"), 12, true, null);

            // Turning it off stops the old service, whose last flush writes the queued click.
            var on = TelemetryHost.Current;
            TelemetryHost.Configure(new TelemetryConfig(), null, spool, _ => false);

            Assert.False(TelemetryHost.Current.IsActive);
            Assert.True(on.Finished.WaitOne(TimeSpan.FromSeconds(10)));
            var file = Assert.Single(Directory.GetFiles(share, "*.jsonl"));
            Assert.Contains("\"title\":\"Clear Clash\"", File.ReadAllText(file));
        }

        [Fact]
        public void Install_DisposesTheSinksOfTheServiceItReplaces()
        {
            var sink = new DisposableSink();
            TelemetryHost.Install(new TelemetryService(new TelemetryConfig { Enabled = true },
                new ITelemetrySink[] { sink }, Path.Combine(_dir, "spool"), () => DateTime.Now, _ => { }));

            var old = TelemetryHost.Install(null);

            Assert.True(old.Finished.WaitOne(TimeSpan.FromSeconds(10)));
            Assert.Equal(new[] { "dispose" }, sink.Events);
        }

        [Fact]
        public void Stop_FlushesWhatIsQueued()
        {
            On();
            TelemetryHost.Finish(TelemetryHost.ForButton(Button(), "click"), 5, true, null);

            TelemetryHost.Stop();

            Assert.Single(_sink.Records);
        }

        [Fact]
        public void FlushWithin_SendsWhatIsQueued_WithoutWaitingForTheWorker()
        {
            On();
            TelemetryHost.Finish(TelemetryHost.ForButton(Button(), "click"), 5, true, null);

            Assert.True(TelemetryHost.FlushWithin(TimeSpan.FromSeconds(5)));
            Assert.Single(_sink.Records);
        }

        [Fact]
        public void FlushWithin_TriesASinkThatIsWaitingOutARetryDelay()
        {
            // pynavis run closes its Navisworks straight afterwards: a record parked for a
            // later retry would wait for some other session to send it.
            var sink = new SwitchSink("http") { Fail = true };
            var service = WithSinks(sink);                                      // its clock never moves on
            service.Record(TelemetryHost.NewRecord("click", "k", "Clear Clash", "", "", ""));
            service.Flush();
            service.Flush();                                                    // a 10 s wait now
            sink.Fail = false;
            service.Record(TelemetryHost.NewRecord("run", "count_items.py", "count_items", "", "", ""));
            TelemetryHost.Install(service);

            Assert.True(TelemetryHost.FlushWithin(TimeSpan.FromSeconds(5)));

            Assert.Equal(3, sink.Attempts);
            Assert.Contains("0 in the spool", service.Status());
        }

        [Fact]
        public void Configure_WithTheSettingsAlreadyInForce_KeepsTheRunningService()
        {
            // Settings' Save configures, then asks for the Reload that configures again.
            var spool = Path.Combine(_dir, "spool");
            var user = new TelemetryConfig { Enabled = true, Folder = Path.Combine(_dir, "share") };
            TelemetryHost.Configure(user, null, spool, _ => false);
            var running = TelemetryHost.Current;

            TelemetryHost.Configure(user.Clone(), null, spool, _ => false);
            Assert.Same(running, TelemetryHost.Current);

            // The same values from the firm's file: the window now shows them read-only.
            var firm = FirmFile("{\"enabled\": true, \"folder\": " + new JavaScriptSerializer().Serialize(user.Folder) + "}");
            TelemetryHost.Configure(user, firm, spool, _ => true);
            Assert.NotSame(running, TelemetryHost.Current);
            Assert.True(TelemetryHost.Current.Config.Managed);

            // A service app-closing stopped is replaced, whatever its settings.
            var managed = TelemetryHost.Current;
            TelemetryHost.Stop();
            TelemetryHost.Configure(user, firm, spool, _ => true);
            Assert.NotSame(managed, TelemetryHost.Current);
        }

        /// <summary>A sink whose sends wait until the test lets them go, like a dead
        /// endpoint or a share that hangs.</summary>
        private sealed class HeldSink : ITelemetrySink
        {
            private int _lines;
            public readonly ManualResetEventSlim Sending = new ManualResetEventSlim();
            public readonly ManualResetEventSlim Release = new ManualResetEventSlim();
            public string Name => "held";
            public string Target => "a held target";
            public int Lines => Volatile.Read(ref _lines);

            public void Send(IReadOnlyList<string> lines)
            {
                Sending.Set();
                Release.Wait(TimeSpan.FromSeconds(10));
                Interlocked.Add(ref _lines, lines.Count);
            }
        }

        private static void WaitFor(Func<bool> condition)
        {
            var clock = Stopwatch.StartNew();
            while (!condition() && clock.Elapsed < TimeSpan.FromSeconds(10)) Thread.Sleep(10);
        }

        [Fact]
        public void Install_DoesNotWaitForTheServiceItReplaces_ToFinishItsLastFlush()
        {
            // Save and Reload configure on the UI thread: a dead endpoint must not freeze
            // Navisworks while the old service's final flush waits on it.
            var held = new HeldSink();
            var old = WithSinks(held);
            TelemetryHost.Install(old);
            old.Record(TelemetryHost.NewRecord("click", "k", "Clear Clash", "", "", ""));

            var clock = Stopwatch.StartNew();
            TelemetryHost.Install(null);
            var took = clock.Elapsed;

            Assert.True(held.Sending.Wait(TimeSpan.FromSeconds(10)));         // its final flush is under way
            held.Release.Set();
            Assert.True(took < TimeSpan.FromMilliseconds(500), "Install took " + took);
            WaitFor(() => held.Lines > 0);
            Assert.Equal(1, held.Lines);                                        // and still delivers
        }

        [Fact]
        public void TheNewWorker_FlushesOnlyOnceTheServiceItReplaced_HasFinishedItsLastFlush()
        {
            // Both read and empty the same spool files, one per kind of destination:
            // flushing side by side they could both send a spooled record.
            var held = new HeldSink();
            var old = WithSinks(held);
            TelemetryHost.Install(old);
            old.Record(TelemetryHost.NewRecord("click", "k", "Clear Clash", "", "", ""));
            var next = _sink.Service(_dir);
            next.Interval = TimeSpan.FromMilliseconds(10);
            next.Record(TelemetryHost.NewRecord("click", "k", "Set Gap", "", "", ""));

            TelemetryHost.Install(next);
            Assert.True(held.Sending.Wait(TimeSpan.FromSeconds(10)));
            Thread.Sleep(200);                                                  // twenty of its intervals
            Assert.Empty(_sink.Records);

            held.Release.Set();
            WaitFor(() => _sink.Records.Count > 0);
            Assert.Equal("Set Gap", Assert.Single(_sink.Records)["title"]);
        }

        [Fact]
        public void Stop_AtAppClosing_AlsoWaitsForAServiceInstallJustReplaced()
        {
            // Settings saved moments before Navisworks closes: the old service's records
            // still get the same bounded wait they had when Install waited for them.
            var held = new HeldSink();
            var old = WithSinks(held);
            TelemetryHost.Install(old);
            old.Record(TelemetryHost.NewRecord("click", "k", "Clear Clash", "", "", ""));
            TelemetryHost.Install(null);
            Assert.True(held.Sending.Wait(TimeSpan.FromSeconds(10)));
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.Sleep(200);
                held.Release.Set();
            });

            TelemetryHost.Stop();

            Assert.Equal(1, held.Lines);
        }

        [Fact]
        public void AppClosing_StopsTelemetry_OnlyOnceTheAppClosingHooksHaveReported()
        {
            // Field, 2026-10-11: the app-closing hook's record never arrived. Boot wires
            // telemetry's stop before the hook runner subscribes, so the service stopped
            // first and the hook's record was queued into a service that had flushed.
            On(includeHooks: true);
            var hub = new EventHub();
            TelemetryHost.StopAfterAppClosing(hub);
            var hook = new HookModel { EventName = "app-closing", ExtensionName = "Firm", EngineId = "ironpython" };
            hub.Raised += e =>
            {
                if (e.Event == NavisEvent.AppClosing)
                    TelemetryHost.Finish(TelemetryHost.ForHook(hook), 4, true, null);
            };

            hub.Raise(new NavisEventArgs { Event = NavisEvent.AppClosing });

            Assert.True(TelemetryHost.Current.IsStopped);
            var line = Assert.Single(_sink.Records);
            Assert.Equal("Firm:app-closing", line["key"]);
        }

        // ---- the firm file ------------------------------------------------------------

        private string FirmFile(string json)
        {
            var path = Path.Combine(_dir, "telemetry.json");
            File.WriteAllText(path, json);
            return path;
        }

        [Fact]
        public void AFirmFileOnlyAdministratorsCanChange_IsTrusted()
        {
            var path = FirmFile("{\"enabled\": true}");

            Assert.True(TelemetryHost.InForce(new TelemetryConfig(), path, _ => true, _logged.Add).Managed);
            Assert.Empty(_logged);
        }

        [Fact]
        public void AFirmFileUsersCanChange_IsIgnored_WithOneLogLineNamingIt()
        {
            // Anyone on the PC could have written it, and it would send everyone's records
            // wherever they chose.
            var path = FirmFile("{\"enabled\": true}");

            var settings = TelemetryHost.InForce(new TelemetryConfig { Url = "https://mine.example" }, path, _ => false, _logged.Add);

            Assert.False(settings.Managed);
            Assert.Equal("https://mine.example", settings.Url);
            var line = Assert.Single(_logged);
            Assert.Contains(path, line);
            Assert.Contains("administrators", line);
        }

        [Fact]
        public void NoFirmFile_IsNoFirmFile_AndNothingIsLogged()
        {
            var path = Path.Combine(_dir, "telemetry.json");

            Assert.False(TelemetryHost.InForce(new TelemetryConfig(), path,
                _ => throw new InvalidOperationException("not asked"), _logged.Add).Managed);
            Assert.False(TelemetryHost.InForce(new TelemetryConfig(), null, _ => true, _logged.Add).Managed);
            Assert.Empty(_logged);
        }

        [Fact]
        public void TheFirmFile_IsCheckedAndReadThroughOneHandle()
        {
            // Checked by its path and then read by its path again, the file could be swapped
            // in between. The check is handed the open file, and nobody can change or replace
            // it until it has been read from that same handle.
            var path = FirmFile("{\"enabled\": true, \"folder\": \"\\\\\\\\firm\\\\share\"}");
            FileStream checkedFile = null;
            Exception swap = null;

            var settings = TelemetryHost.InForce(new TelemetryConfig(), path, stream =>
            {
                checkedFile = stream;
                try { File.WriteAllText(path, "{\"enabled\": true, \"folder\": \"C:\\\\planted\"}"); }
                catch (Exception ex) { swap = ex; }
                return true;
            }, _logged.Add);

            Assert.Equal(path, checkedFile.Name);
            Assert.IsAssignableFrom<IOException>(swap);
            Assert.True(settings.Managed);
            Assert.Equal(@"\\firm\share", settings.Folder);
        }

        [Fact]
        public void InForce_TakesATrustedFirmFile_AndIgnoresAnUntrustedOne()
        {
            var path = Path.Combine(_dir, "telemetry.json");
            File.WriteAllText(path, "{\"enabled\": true, \"folder\": \"\\\\\\\\firm\\\\share\"}");
            var user = new TelemetryConfig { Enabled = false, Url = "https://mine.example" };

            var trusted = TelemetryHost.InForce(user, path, _ => true, _logged.Add);
            var untrusted = TelemetryHost.InForce(user, path, _ => false, _logged.Add);

            Assert.True(trusted.Managed);
            Assert.True(trusted.Enabled);
            Assert.Equal(@"\\firm\share", trusted.Folder);
            Assert.Equal("", trusted.Url);
            Assert.False(untrusted.Managed);
            Assert.Equal("https://mine.example", untrusted.Url);
        }

        // ---- what a run records ----------------------------------------------------------

        [Fact]
        public void Wants_LeavesHooksOut_UnlessIncludeHooksIsOn()
        {
            On();
            Assert.True(TelemetryHost.Wants("click"));
            Assert.False(TelemetryHost.Wants("hook"));

            On(includeHooks: true);
            Assert.True(TelemetryHost.Wants("hook"));
        }

        [Fact]
        public void AClick_IsRecordedWithTheButtonsKeyTitleExtensionAndEngine()
        {
            On();
            var before = DateTimeOffset.Now.AddSeconds(-1);

            TelemetryHost.Finish(TelemetryHost.ForButton(Button(), "config"), 1234, true, null);

            var line = OnlyRecord();
            Assert.Equal("config", line["kind"]);
            Assert.Equal("pyNavis.tab/Clash.panel/Clear_Clash.pushbutton", line["key"]);
            Assert.Equal("Clear Clash", line["title"]);
            Assert.Equal("pyNavis", line["extension"]);
            Assert.Equal("cpython", line["engine"]);
            Assert.Equal(1234, line["ms"]);
            Assert.Equal("ok", line["result"]);
            Assert.Equal("", line["error"]);
            Assert.Equal(TelemetryRecord.CurrentSession, line["session"]);
            Assert.Equal(Environment.UserName, line["user"]);
            Assert.Equal(Environment.MachineName, line["machine"]);
            Assert.Equal(PyNavisVersion.Product, line["pynavis"]);
            Assert.Equal(PyNavisVersion.HostYear > 0 ? PyNavisVersion.HostYear.ToString() : "", line["navis"]);
            Assert.Equal("", line["doc"]);                     // no Navisworks here, so no document
            Assert.True(DateTimeOffset.Parse((string)line["time"]) >= before);
        }

        [Fact]
        public void AFailedRun_RecordsTheErrorsSummary()
        {
            On();

            TelemetryHost.Finish(TelemetryHost.ForButton(Button(), "click"), 40, false,
                "Traceback (most recent call last):\n  File \"script.py\", line 3, in <module>\nKeyError: 'Level'\n");

            var line = OnlyRecord();
            Assert.Equal("error", line["result"]);
            Assert.Equal("KeyError: 'Level'", line["error"]);
        }

        [Fact]
        public void AHook_IsRecordedUnderItsIdAndEvent()
        {
            On(includeHooks: true);
            var hook = new HookModel { EventName = "doc-opened", ExtensionName = "Firm", EngineId = "ironpython" };

            TelemetryHost.Finish(TelemetryHost.ForHook(hook), 8, true, null);

            var line = OnlyRecord();
            Assert.Equal("hook", line["kind"]);
            Assert.Equal("Firm:doc-opened", line["key"]);
            Assert.Equal("doc-opened", line["title"]);
            Assert.Equal("Firm", line["extension"]);
            Assert.Equal("ironpython", line["engine"]);
        }

        [Fact]
        public void AHook_IsNotEvenBuilt_WhileHooksAreLeftOut()
        {
            On();
            Assert.Null(TelemetryHost.ForHook(new HookModel { EventName = "selection-changed", ExtensionName = "Firm" }));
        }

        [Fact]
        public void Finish_OfNothing_DoesNothing_AndNeverThrows()
        {
            On();
            TelemetryHost.Finish(null, 1, true, null);
            TelemetryHost.Current.Flush();
            Assert.Empty(_sink.Records);
        }

        [Fact]
        public void AClick_OfAButtonThatDoesNotNameItsExtension_RecordsNoExtension()
        {
            // The bundle key starts with the tab folder, which is not the extension.
            On();
            var button = Button();
            button.ExtensionName = null;

            TelemetryHost.Finish(TelemetryHost.ForButton(button, "click"), 3, true, null);

            Assert.Equal("", OnlyRecord()["extension"]);
        }

        [Fact]
        public void TheTitle_IsOneLine()
        {
            Assert.Equal("Export Viewpoints", TelemetryHost.OneLine("Export\nViewpoints"));
            Assert.Equal("Export Viewpoints", TelemetryHost.OneLine("  Export \r\n\r\n Viewpoints \n"));
            Assert.Equal("", TelemetryHost.OneLine(null));
        }

        [Fact]
        public void TheExtension_IsTheModelsOwnName_OrNothing()
        {
            Assert.Equal("pyNavis", TelemetryHost.ExtensionOf(" pyNavis "));
            Assert.Equal("", TelemetryHost.ExtensionOf(null));
            Assert.Equal("", TelemetryHost.ExtensionOf(" "));
        }

        [Fact]
        public void ARunOfABundlesScript_TakesThatButtonsKeyTitleAndExtension()
        {
            var button = Button();
            var panel = new PanelModel { Title = "Clash" };
            panel.Items.Add(button);
            var tab = new TabModel { Title = "pyNavis" };
            tab.Panels.Add(panel);
            var extension = new ExtensionModel { Name = "pyNavis" };
            extension.Tabs.Add(tab);

            var known = TelemetryHost.RunIdentity(button.ScriptPath.ToUpperInvariant(), new[] { extension });
            var loose = TelemetryHost.RunIdentity(@"C:\scripts\count_items.py", new[] { extension });

            Assert.Equal((button.BundleKey, "Clear Clash", "pyNavis"), known);
            Assert.Equal(("count_items.py", "count_items", ""), loose);
        }

        // ---- Send test record ----------------------------------------------------------

        [Fact]
        public void SendTestRecord_GoesThroughTheServiceInForce_WhenTheWindowShowsItsSettings()
        {
            var service = _sink.Service(_dir);

            var status = TelemetryHost.SendTestRecord(service.Config, service, _dir);

            var line = Assert.Single(_sink.Records);
            Assert.Equal("test", line["kind"]);
            Assert.Equal("test", line["key"]);
            Assert.Equal("Test record", line["title"]);
            Assert.Equal("Test record sent to the test sink.", status);
        }

        /// <summary>A sink the test switches between failing and working.</summary>
        private sealed class SwitchSink : ITelemetrySink
        {
            public SwitchSink(string name) { Name = name; }
            public string Name { get; }
            public string Target => "the " + Name + " target";
            public bool Fail;
            public int Attempts;
            public void Send(IReadOnlyList<string> lines)
            {
                Attempts++;
                if (Fail) throw new IOException("the share is offline");
            }
        }

        private TelemetryService WithSinks(params ITelemetrySink[] sinks)
        {
            var now = new DateTime(2026, 10, 10, 14, 5, 0);
            return new TelemetryService(new TelemetryConfig { Enabled = true }, sinks,
                Path.Combine(_dir, "spool"), () => now, _ => { });
        }

        [Fact]
        public void SendTestRecord_IsSentAtOnce_EvenWhileTheServiceWaitsToRetry()
        {
            var sink = new SwitchSink("folder") { Fail = true };
            var service = WithSinks(sink);
            service.Record(TelemetryHost.NewRecord("click", "k", "Clear Clash", "", "", ""));
            service.Flush();
            service.Flush();                                                    // a 10 s wait now
            sink.Fail = false;

            var status = TelemetryHost.SendTestRecord(service.Config, service, _dir);

            Assert.Equal(3, sink.Attempts);
            Assert.Equal("Test record sent to the folder target.", status);
        }

        [Fact]
        public void SendTestRecord_IsNotTakenByTheWorkersOwnFlush()
        {
            // Queued and then flushed, the test record could be taken by the worker's flush
            // in between: the flush that was to send it found nothing, and the window said
            // it was not sent although it had arrived. The clock is read inside the flush
            // lock, so it holds the worker's first flush there until the test's own send is
            // waiting for the lock, then lets the worker drain whatever is queued.
            var tester = Thread.CurrentThread;
            var inFlush = new ManualResetEventSlim();
            var go = new ManualResetEventSlim();
            var held = 0;
            Func<DateTime> clock = () =>
            {
                if (Thread.CurrentThread != tester && Interlocked.Exchange(ref held, 1) == 0)
                {
                    inFlush.Set();
                    go.Wait(TimeSpan.FromSeconds(10));
                    WaitFor(() => (tester.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0);
                }
                return DateTime.Now;
            };
            var service = new TelemetryService(new TelemetryConfig { Enabled = true }, new ITelemetrySink[] { _sink },
                Path.Combine(_dir, "spool"), clock, _ => { }) { Interval = TimeSpan.FromMilliseconds(1) };
            service.Start();
            try
            {
                Assert.True(inFlush.Wait(TimeSpan.FromSeconds(10)));
                go.Set();

                var status = TelemetryHost.SendTestRecord(service.Config, service, _dir);

                Assert.Equal("Test record sent to the test sink.", status);
                Assert.Equal("test", Assert.Single(_sink.Records)["kind"]);
            }
            finally
            {
                service.Stop();
            }
        }

        [Fact]
        public void SendTestRecord_ThatFails_SaysWhereAndWhy_AndWhereItDidGo()
        {
            var folder = new SwitchSink("folder");
            var http = new SwitchSink("http") { Fail = true };

            Assert.Equal("Test record could not be sent to the http target: the share is offline. "
                         + "It reached the folder target.",
                TelemetryHost.SendTestRecord(new TelemetryConfig { Enabled = true }, WithSinks(folder, http), _dir));

            folder.Fail = true;
            Assert.Equal("Test record could not be sent to the folder target: the share is offline. "
                         + "Test record could not be sent to the http target: the share is offline.",
                TelemetryHost.SendTestRecord(new TelemetryConfig { Enabled = true }, WithSinks(folder, http), _dir));
        }

        [Fact]
        public void SendTestRecord_OfSettingsNotSavedYet_SendsToWhatTheWindowShows()
        {
            var share = Path.Combine(_dir, "share");
            var entered = new TelemetryConfig { Enabled = true, Folder = share };

            var status = TelemetryHost.SendTestRecord(entered, _sink.Service(_dir), _dir);

            Assert.Equal("Test record sent. Save to keep these settings.", status);
            Assert.Empty(_sink.Records);
            var file = Assert.Single(Directory.GetFiles(share, "*.jsonl"));
            Assert.Contains("\"kind\":\"test\"", File.ReadAllText(file));
            // The one-off service's spool goes with it.
            Assert.Empty(Directory.GetDirectories(_dir, "pyNavis-telemetry-test-*"));
        }

        [Fact]
        public void SendTestRecord_WithTelemetryOff_StillSends_AndSaysItStaysOff()
        {
            var share = Path.Combine(_dir, "share");

            var status = TelemetryHost.SendTestRecord(
                new TelemetryConfig { Enabled = false, Folder = share }, TelemetryService.Create(null, _dir, null), _dir);

            Assert.Equal("Test record sent. Telemetry stays off until you turn it on and save.", status);
            Assert.Single(Directory.GetFiles(share, "*.jsonl"));
        }

        [Fact]
        public void SendTestRecord_OfAFirmsSettingsThatAreOff_SaysTheFirmHasItOff()
        {
            var share = Path.Combine(_dir, "share");
            var firm = TelemetryService.Create(
                new TelemetryConfig { Enabled = false, Folder = share, Managed = true }, _dir, null);

            var status = TelemetryHost.SendTestRecord(firm.Config, firm, _dir);

            Assert.Equal("Test record sent. Your firm has telemetry turned off.", status);
            Assert.Single(Directory.GetFiles(share, "*.jsonl"));
        }

        [Fact]
        public void SendTestRecord_SaysWhatIsMissing_OrWhatFailed()
        {
            var off = TelemetryService.Create(null, _dir, null);

            Assert.Equal("Name a folder or a URL first.",
                TelemetryHost.SendTestRecord(new TelemetryConfig { Enabled = true }, off, _dir));
            Assert.Equal("The URL must start with http:// or https://.",
                TelemetryHost.SendTestRecord(new TelemetryConfig { Enabled = true, Url = "ftp://firm" }, off, _dir));

            // A folder that cannot be created: a file is in the way.
            var blocked = Path.Combine(_dir, "blocked");
            File.WriteAllText(blocked, "");
            var status = TelemetryHost.SendTestRecord(
                new TelemetryConfig { Enabled = true, Folder = Path.Combine(blocked, "share") }, off, _dir);
            Assert.StartsWith("Test record could not be sent to " + Path.Combine(blocked, "share") + ": ", status);
        }

        /// <summary>
        /// Every runtime path that runs a script with __pynavis__ (click, Shift+Click,
        /// smartbutton init, dock pane, hook, startup.py, pynavis run) must run it inside a
        /// telemetry scope: its own record, or null so a note cannot land in an outer run's.
        /// Most of those paths need Navisworks, so the rule is checked on the sources, as
        /// RunExecutorTests does for __runner__.
        /// </summary>
        [Fact]
        public void EveryScriptPathThatSetsPyNavis_RunsInsideATelemetryScope()
        {
            string runtimeSrc = null;
            for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "src", "PyNavis.Runtime");
                if (Directory.Exists(candidate)) { runtimeSrc = candidate; break; }
            }
            Assert.NotNull(runtimeSrc);

            var setters = Directory.GetFiles(runtimeSrc, "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                .Where(p => File.ReadAllText(p).Contains("Globals[\"__pynavis__\"]"))
                .ToArray();

            Assert.True(setters.Length >= 6, "Expected the six script paths, found " + setters.Length);
            Assert.Empty(setters
                .Where(p => !File.ReadAllText(p).Contains("TelemetryScope("))
                .Select(Path.GetFileName));
        }

        // ---- pynavis.telemetry.note ------------------------------------------------------

        private static TelemetryRecord Current() => new TelemetryRecord { Kind = "click", Title = "Tool" };

        [Fact]
        public void Note_OutsideARun_DoesNothing()
        {
            Assert.False(PyNavisHost.Instance.TelemetryNote(new Hashtable { ["items"] = 3 }));
            Assert.False(PyNavisHost.Instance.TelemetryNoteJson("{\"items\": 3}"));
        }

        [Fact]
        public void Note_MergesIntoTheRunsRecord_AndARepeatedNameReplacesTheValue()
        {
            var record = Current();
            PyNavisHost.Instance.BeginTelemetry(record);
            try
            {
                Assert.True(PyNavisHost.Instance.TelemetryNote(new Hashtable { ["items"] = 3, ["level"] = "L1" }));
                Assert.True(PyNavisHost.Instance.TelemetryNote(new Hashtable { ["items"] = 5 }));
            }
            finally
            {
                Assert.Same(record, PyNavisHost.Instance.EndTelemetry());
            }

            Assert.Equal(5, record.Results["items"]);
            Assert.Equal("L1", record.Results["level"]);
            Assert.False(PyNavisHost.Instance.TelemetryNote(new Hashtable { ["late"] = 1 }));
            Assert.False(record.Results.ContainsKey("late"));
        }

        [Fact]
        public void Note_CopiesTheValues_SoChangingThemLaterChangesNothing()
        {
            var record = Current();
            var names = new List<object> { "Duct" };
            PyNavisHost.Instance.BeginTelemetry(record);
            try
            {
                PyNavisHost.Instance.TelemetryNote(new Hashtable { ["names"] = names });
                names.Add("Pipe");
            }
            finally
            {
                PyNavisHost.Instance.EndTelemetry();
            }

            Assert.Equal(new object[] { "Duct" }, ((IEnumerable)record.Results["names"]).Cast<object>().ToArray());
        }

        [Fact]
        public void ARunThatIsNotRecorded_KeepsItsNotesOutOfTheRunAroundIt()
        {
            // startup.py during a Reload click, a smartbutton refreshing at ribbon build:
            // their notes must not land in the Reload button's record.
            var outer = Current();
            PyNavisHost.Instance.BeginTelemetry(outer);
            try
            {
                PyNavisHost.Instance.BeginTelemetry(null);
                Assert.False(PyNavisHost.Instance.TelemetryNote(new Hashtable { ["inner"] = 1 }));
                Assert.Null(PyNavisHost.Instance.EndTelemetry());

                Assert.True(PyNavisHost.Instance.TelemetryNote(new Hashtable { ["outer"] = 2 }));
            }
            finally
            {
                PyNavisHost.Instance.EndTelemetry();
            }

            Assert.Equal(new[] { "outer" }, outer.Results.Keys.ToArray());
        }

        [Fact]
        public void NoteJson_ArrivesAsPlainValues()
        {
            var record = Current();
            PyNavisHost.Instance.BeginTelemetry(record);
            try
            {
                Assert.True(PyNavisHost.Instance.TelemetryNoteJson(
                    "{\"items\": 4, \"ratio\": 0.5, \"ok\": true, \"none\": null, \"names\": [\"Duct\", 2], \"by\": {\"L1\": 3}}"));
                Assert.False(PyNavisHost.Instance.TelemetryNoteJson("[1, 2]"));
                Assert.False(PyNavisHost.Instance.TelemetryNoteJson("not json"));
            }
            finally
            {
                PyNavisHost.Instance.EndTelemetry();
            }

            var results = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(
                new TelemetryRecord { Results = record.Results }.ToJsonLine())["results"] as Dictionary<string, object>;
            Assert.NotNull(results);
            Assert.Equal(4, results["items"]);
            Assert.Equal(0.5m, Convert.ToDecimal(results["ratio"]));
            Assert.Equal(true, results["ok"]);
            Assert.Null(results["none"]);
            Assert.Equal(new object[] { "Duct", 2 }, ((IEnumerable)results["names"]).Cast<object>().ToArray());
            Assert.Equal(3, ((Dictionary<string, object>)results["by"])["L1"]);
        }

        [Fact]
        public void ThePythonModule_NotesInsideARun_AndIsQuietOutsideOne()
        {
            var engine = new IronPythonEngine();
            var config = new EngineConfig();
            var stdlib = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(stdlib)) config.SearchPaths.Add(stdlib);
            config.SearchPaths.Add(PyNavisLibTests.PyNavisLibDir);
            engine.Initialize(config);

            var outside = engine.Execute(new ScriptRequest { Code = "from pynavis import telemetry\nr = telemetry.note(items=1)\nassert r is None" });
            Assert.True(outside.Succeeded, outside.ErrorText);

            var record = Current();
            PyNavisHost.Instance.BeginTelemetry(record);
            ExecResult inside;
            try
            {
                inside = engine.Execute(new ScriptRequest
                {
                    Code = "import System\n"
                         + "from pynavis import telemetry\n"
                         + "telemetry.note(items=3, minutes_saved=10)\n"
                         + "telemetry.note(items=4, ratio=0.25, ok=True, level=None, nan=float('nan'),\n"
                         + "               names=('Duct', 'Pipe'), by={'L1': 2, 3: 'three'}, when=System.Guid.Empty)\n"
                         + "telemetry.note()\n",
                });
            }
            finally
            {
                PyNavisHost.Instance.EndTelemetry();
            }

            Assert.True(inside.Succeeded, inside.ErrorText);
            var results = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(
                new TelemetryRecord { Results = record.Results }.ToJsonLine())["results"] as Dictionary<string, object>;
            Assert.NotNull(results);
            Assert.Equal(4, results["items"]);
            Assert.Equal(10, results["minutes_saved"]);
            Assert.Equal(0.25m, Convert.ToDecimal(results["ratio"]));
            Assert.Equal(true, results["ok"]);
            Assert.Null(results["level"]);
            Assert.Null(results["nan"]);
            Assert.Equal(new object[] { "Duct", "Pipe" }, ((IEnumerable)results["names"]).Cast<object>().ToArray());
            var by = (Dictionary<string, object>)results["by"];
            Assert.Equal(2, by["L1"]);
            Assert.Equal("three", by["3"]);
            Assert.Equal(Guid.Empty.ToString(), results["when"]);   // not JSON, so its str()
        }
    }
}
