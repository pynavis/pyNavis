using System;
using System.IO;
using System.Threading;
using System.Windows;
using PyNavis.Runtime.Bundles;
using PyNavis.Runtime.Config;
using PyNavis.Runtime.Panes;
using Xunit;

namespace PyNavis.Tests
{
    public class PaneRegistryTests : IDisposable
    {
        private readonly string _root;
        private readonly string _configPath;

        public PaneRegistryTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "pynavis_tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _configPath = Path.Combine(_root, "config.json");
        }

        public void Dispose()
        {
            PaneRegistry.SlotProbe = null;
            try { Directory.Delete(_root, true); } catch { }
        }

        /// <summary>Writes a config asking for extra satellite slots.</summary>
        private PyNavisConfig ConfigWithExtraSlots(int extra)
        {
            File.WriteAllText(_configPath,
                "{ \"panes\": { \"extraSlots\": " + extra + " } }");
            return PyNavisConfig.Load(_configPath);
        }

        [Fact]
        public void SlotCount_Counts_Only_The_Slots_Navisworks_Actually_Registered()
        {
            // The satellite can fail to load with no error anywhere. Believing config
            // would hand a bundle a slot with no plugin behind it, and the pane would
            // then never open with nothing explaining why.
            PaneRegistry.SlotProbe = slot => false;
            PaneRegistry.Configure(new ExtensionModel[0], ConfigWithExtraSlots(3), _configPath);

            Assert.Equal(PaneRegistry.ShippedSlots, PaneRegistry.SlotCount);
        }

        [Fact]
        public void SlotCount_Accepts_The_Extra_Slots_When_The_Satellite_Did_Load()
        {
            PaneRegistry.SlotProbe = slot => true;
            PaneRegistry.Configure(new ExtensionModel[0], ConfigWithExtraSlots(3), _configPath);

            Assert.Equal(PaneRegistry.ShippedSlots + 3, PaneRegistry.SlotCount);
        }

        [Fact]
        public void SlotCount_Stops_At_The_First_Gap_In_A_Partly_Loaded_Satellite()
        {
            // Report the prefix that works rather than an optimistic total.
            PaneRegistry.SlotProbe = slot => slot <= PaneRegistry.ShippedSlots + 1;
            PaneRegistry.Configure(new ExtensionModel[0], ConfigWithExtraSlots(3), _configPath);

            Assert.Equal(PaneRegistry.ShippedSlots + 1, PaneRegistry.SlotCount);
        }

        private ExtensionModel Extension(params string[] paneNames)
        {
            foreach (var name in paneNames)
            {
                var dir = Path.Combine(_root, "E.extension", "T.tab", "P.panel", name + ".dockpane");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "pane.xaml"),
                    "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />");
            }
            return BundleParser.ParseExtension(Path.Combine(_root, "E.extension"));
        }

        [Fact]
        public void Configure_Claims_Slots_And_Persists_Them()
        {
            var ext = Extension("A", "B");
            PaneRegistry.Configure(new[] { ext }, PyNavisConfig.Load(_configPath), _configPath);

            Assert.Equal(1, PaneRegistry.SlotFor("T.tab/P.panel/A.dockpane"));
            Assert.Equal(2, PaneRegistry.SlotFor("T.tab/P.panel/B.dockpane"));
            Assert.Equal("T.tab/P.panel/A.dockpane", PaneRegistry.BundleKeyFor(1));

            var saved = PyNavisConfig.Load(_configPath);
            Assert.Equal(1, saved.PaneAssignments["T.tab/P.panel/A.dockpane"]);
        }

        [Fact]
        public void Configure_Claims_A_Slot_For_A_Dockpane_Inside_A_Slideout()
        {
            var dir = Path.Combine(_root, "E.extension", "T.tab", "P.panel", "More.slideout", "S.dockpane");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "pane.xaml"),
                "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />");
            var ext = BundleParser.ParseExtension(Path.Combine(_root, "E.extension"));
            Assert.IsType<DockPaneModel>(Assert.Single(ext.Tabs[0].Panels[0].Slideout));

            PaneRegistry.Configure(new[] { ext }, PyNavisConfig.Load(_configPath), _configPath);

            Assert.Equal(1, PaneRegistry.SlotFor("T.tab/P.panel/More.slideout/S.dockpane"));
        }

        [Fact]
        public void An_Unclaimed_Slot_Has_No_Bundle_Key()
        {
            PaneRegistry.Configure(new[] { Extension("A") }, PyNavisConfig.Load(_configPath), _configPath);
            Assert.Null(PaneRegistry.BundleKeyFor(5));
        }

        [Fact]
        public void VisibilityChanged_Raises_The_Event_With_The_Bundle_Key()
        {
            PaneRegistry.Configure(new[] { Extension("A") }, PyNavisConfig.Load(_configPath), _configPath);

            string seenKey = null;
            var seenVisible = false;
            Action<string, bool> handler = (key, visible) => { seenKey = key; seenVisible = visible; };
            PaneRegistry.VisibleChanged += handler;
            try
            {
                PaneRegistry.VisibilityChanged(1, true);
            }
            finally
            {
                PaneRegistry.VisibleChanged -= handler;
            }

            Assert.Equal("T.tab/P.panel/A.dockpane", seenKey);
            Assert.True(seenVisible);
        }

        /// <summary>
        /// Names the reason CreatePane and Configure are separate entry points: Navisworks
        /// restores previously-open dock panes during startup, which can happen before the
        /// ribbon exists and therefore before Configure has ever run. Uses slot 4, which no
        /// other test in this class ever claims, so this test's "before Configure" moment is
        /// guaranteed to see an empty Claims map regardless of run order (PaneRegistry's
        /// Shells/Claims are static and shared across every test in the process; slot 4 is
        /// how this test stays independent of that without touching production code).
        /// </summary>
        [Fact]
        public void CreatePane_Before_Configure_Fills_The_Shell_Once_Configure_Runs()
        {
            File.WriteAllText(_configPath,
                "{\"panes\":{\"assignments\":{\"T.tab/P.panel/Z.dockpane\":4}}}");
            var ext = Extension("Z");

            OnSta(() =>
            {
                var shell = (PaneShell)PaneRegistry.CreatePane(4);
                try
                {
                    var placeholder = shell.CurrentContent;
                    Assert.NotNull(placeholder);

                    PaneRegistry.Configure(new[] { ext }, PyNavisConfig.Load(_configPath), _configPath);

                    Assert.NotSame(placeholder, shell.CurrentContent);
                }
                finally
                {
                    // Shells is static and outlives this test; leaving slot 4 claimed
                    // would make a later test's Configure() try to WPF-fill it off the
                    // wrong thread.
                    PaneRegistry.Released(4);
                    shell.Dispose();
                }
            });
        }

        /// <summary>
        /// A PaneShell subclass whose SetContent - the last thing Fill does before
        /// returning - calls back into CreatePane for its own slot once, simulating what
        /// "open my panel on load" does in practice
        /// (pynavis.panes.show()/__pane__.Visible = True -> SetVisible -> Navisworks ->
        /// CreateControlPane -> Fill, again). Reaching this through a real script.py would
        /// need EngineManager, the global singleton EngineManagerInvalidationTests already
        /// owns exclusively in this test process; hooking SetContent instead reaches the
        /// exact same reentrant call into Fill without touching it.
        /// </summary>
        private sealed class ReentrantShell : PaneShell
        {
            private readonly int _slot;
            private bool _armed = true;

            /// <summary>How many times this override actually ran. The guard being
            /// tested lives in Fill, not in this shell: Fill only calls shell.SetContent
            /// at all when it wins the Filling.Add(slot) race, so this counts how many
            /// times Fill got that far for this slot. With the guard in place the
            /// reentrant CreatePane(slot) call below finds the slot already in Filling
            /// and returns without ever reaching shell.SetContent, so this stays at 1.
            /// With the guard removed, the reentrant Fill call proceeds and calls
            /// shell.SetContent again (disarmed, so it does not recurse a third time),
            /// making this 2 - and because that inner call's base.SetContent runs before
            /// the outer call's, CurrentContent ends up holding the outer content either
            /// way, which is exactly why asserting on CurrentContent alone does not
            /// discriminate the two cases.
            /// </summary>
            public int SetContentCallCount { get; private set; }

            public ReentrantShell(int slot) : base(slot) { _slot = slot; }

            public override void SetContent(UIElement content)
            {
                SetContentCallCount++;
                if (_armed)
                {
                    _armed = false;
                    // Still inside the OUTER Fill(slot, this) call - Filling.Remove(slot)
                    // has not run yet.
                    PaneRegistry.CreatePane(_slot);
                }
                base.SetContent(content);
            }
        }

        /// <summary>Finding 5: a pane's script.py runs inside Fill, and Fill must refuse a
        /// reentrant call for the slot it is already filling.</summary>
        [Fact]
        public void Fill_Refuses_A_Reentrant_Call_For_The_Same_Slot()
        {
            const int slot = 5;
            var ext = Extension("Loop");
            // Pin the claim to slot 5 rather than letting Resolve auto-assign, so this
            // test stays independent of what other tests in this class have claimed.
            File.WriteAllText(_configPath,
                "{\"panes\":{\"assignments\":{\"T.tab/P.panel/Loop.dockpane\":" + slot + "}}}");
            PaneRegistry.Configure(new[] { ext }, PyNavisConfig.Load(_configPath), _configPath);
            Assert.Equal(slot, PaneRegistry.SlotFor("T.tab/P.panel/Loop.dockpane"));

            OnSta(() =>
            {
                var shell = new ReentrantShell(slot);
                PaneRegistry.SeedShellForTest(slot, shell);
                try
                {
                    PaneRegistry.CreatePane(slot);

                    Assert.NotNull(shell.CurrentContent);
                    // The real proof: the reentrant CreatePane(5) call must never have
                    // reached shell.SetContent at all, because Fill refused it before
                    // touching the shell. If the guard is deleted, the reentrant Fill
                    // call proceeds and this becomes 2 even though CurrentContent above
                    // still passes (see SetContentCallCount's doc comment).
                    Assert.Equal(1, shell.SetContentCallCount);
                }
                finally
                {
                    PaneRegistry.Released(slot);
                    shell.Dispose();
                }
            });
        }

        [Fact]
        public void Configure_Keeps_A_Vanished_Bundles_Slot_Claim_Instead_Of_Reassigning_It()
        {
            // "Ghost" no longer exists as an extension (its *.dockpane folder is gone),
            // but its slot 3 claim is still in config.json from when it did.
            File.WriteAllText(_configPath,
                "{\"panes\":{\"assignments\":{\"T.tab/P.panel/Ghost.dockpane\":3}}}");
            var ext = Extension("Live");

            PaneRegistry.Configure(new[] { ext }, PyNavisConfig.Load(_configPath), _configPath);

            var saved = PyNavisConfig.Load(_configPath);
            Assert.Equal(3, saved.PaneAssignments["T.tab/P.panel/Ghost.dockpane"]);
            Assert.NotEqual(3, saved.PaneAssignments["T.tab/P.panel/Live.dockpane"]);
        }

        private static void OnSta(Action action)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException("STA action failed: " + failure);
        }
    }
}
