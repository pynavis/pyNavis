using System;
using System.IO;
using System.Threading;
using System.Windows.Threading;
using PyNavis.Runtime.Config;
using PyNavis.Runtime.Forms;
using PyNavis.Runtime.Telemetry;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The Settings window's Telemetry section: the user's own settings in, the edits out,
    /// a firm's settings shown but never saved over the user's, and Send test record
    /// answering on the status line. The window reads TelemetryHost.Current, which
    /// TelemetryHostTests swap, so these run in that non-parallel collection.
    /// </summary>
    [Collection(RunnerContextCollection.Name)]
    public class SettingsDialogTelemetryTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "pynavis_tests", Guid.NewGuid().ToString("N"));

        public SettingsDialogTelemetryTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private static PyNavisConfig.UserSettings With(TelemetryConfig telemetry) =>
            new PyNavisConfig.UserSettings { Telemetry = telemetry };

        [Fact]
        public void TheSection_ShowsTheUsersOwnSettings_AndCollectsTheEdits()
        {
            var mine = new TelemetryConfig { Enabled = true, Folder = @"\\server\bim", IncludeHooks = true };

            OnSta(() =>
            {
                var window = SettingsDialog.Build(With(mine), false, null, new TelemetryConfig());
                var back = SettingsDialog.CollectForTest(window).Telemetry;

                Assert.NotNull(back);
                Assert.True(back.Enabled);
                Assert.Equal(@"\\server\bim", back.Folder);
                Assert.Equal("", back.Url);
                Assert.True(back.IncludeHooks);
                Assert.False(SettingsDialog.TelemetryIsReadOnlyForTest(window));
                Assert.Null(SettingsDialog.TelemetryManagedNoteOf(window));

                SettingsDialog.SetTelemetryForTest(window, false, "  ", " https://bim.example/pynavis ", false);
                back = SettingsDialog.CollectForTest(window).Telemetry;
                Assert.False(back.Enabled);
                Assert.Equal("", back.Folder);
                Assert.Equal("https://bim.example/pynavis", back.Url);
                Assert.False(back.IncludeHooks);
                window.Close();
            });
        }

        [Fact]
        public void ANullSettings_StillCollectsTelemetry_SoSavingNeverDropsTheSection()
        {
            OnSta(() =>
            {
                var window = SettingsDialog.Build(null, false, null, new TelemetryConfig());
                var back = SettingsDialog.CollectForTest(window).Telemetry;

                Assert.NotNull(back);
                Assert.False(back.Enabled);
                window.Close();
            });
        }

        [Fact]
        public void AFirmsSettings_AreShownReadOnly_AndTheUsersOwnAreWhatIsSaved()
        {
            var mine = new TelemetryConfig { Enabled = false, Url = "https://mine.example" };
            var firm = new TelemetryConfig { Enabled = true, Folder = @"\\firm\telemetry", Managed = true };

            OnSta(() =>
            {
                var window = SettingsDialog.Build(With(mine), false, null, firm);

                Assert.True(SettingsDialog.TelemetryIsReadOnlyForTest(window));
                Assert.Equal("Managed by your firm from " + TelemetryHost.FirmPath + ".",
                    SettingsDialog.TelemetryManagedNoteOf(window));
                Assert.Equal(@"\\firm\telemetry", SettingsDialog.TelemetryFolderOf(window));

                var back = SettingsDialog.CollectForTest(window).Telemetry;
                Assert.False(back.Enabled);
                Assert.Equal("", back.Folder);
                Assert.Equal("https://mine.example", back.Url);
                window.Close();
            });
        }

        [Fact]
        public void TheStatusLine_StartsAsTheServicesOwn()
        {
            OnSta(() =>
            {
                var window = SettingsDialog.Build(With(new TelemetryConfig()), false, null, new TelemetryConfig());

                Assert.Equal(TelemetryHost.Current.Status(), SettingsDialog.TelemetryStatusOf(window));
                window.Close();
            });
        }

        [Fact]
        public void SendTestRecord_SendsWhatTheWindowShows_AndSaysSoOnTheStatusLine()
        {
            var share = Path.Combine(_dir, "share");

            OnSta(() =>
            {
                var window = SettingsDialog.Build(With(new TelemetryConfig()), false, null, new TelemetryConfig());
                SettingsDialog.SetTelemetryForTest(window, true, share, "", false);

                SettingsDialog.PressSendTestRecordForTest(window);
                Pump(() => SettingsDialog.TelemetryStatusOf(window).StartsWith("Test record sent"), TimeSpan.FromSeconds(20));

                Assert.Equal("Test record sent. Save to keep these settings.", SettingsDialog.TelemetryStatusOf(window));
                window.Close();
            });

            var file = Assert.Single(Directory.GetFiles(share, "*.jsonl"));
            Assert.Contains("\"kind\":\"test\"", File.ReadAllText(file));
        }

        /// <summary>Runs the dispatcher until <paramref name="done"/> or the time runs out,
        /// so a background task's answer posted through it lands.</summary>
        private static void Pump(Func<bool> done, TimeSpan limit)
        {
            var frame = new DispatcherFrame();
            var deadline = DateTime.UtcNow + limit;
            var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(20), DispatcherPriority.Background,
                (s, e) => { if (done() || DateTime.UtcNow > deadline) frame.Continue = false; },
                Dispatcher.CurrentDispatcher);
            timer.Start();
            Dispatcher.PushFrame(frame);
            timer.Stop();
        }

        private static void OnSta(Action body)
        {
            Exception failure = null;
            var thread = new Thread(() => { try { body(); } catch (Exception ex) { failure = ex; } });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }
    }
}
