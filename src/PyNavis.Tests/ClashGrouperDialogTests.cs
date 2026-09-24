using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using PyNavis.Runtime.Forms;
using Xunit;
using Config = PyNavis.Runtime.Forms.ClashGrouperDialog.GrouperConfig;
using Preview = PyNavis.Runtime.Forms.ClashGrouperDialog.PreviewData;

namespace PyNavis.Tests
{
    /// <summary>
    /// Grouper dialog seam tests: STA construction (never shown), config capture,
    /// preview plumbing, and enable/disable rules. The preview provider is a
    /// plain lambda standing in for the python engine.
    /// </summary>
    public class ClashGrouperDialogTests
    {
        private static List<ClashGrouperDialog.TestRow> Tests(params (string name, int total, bool on)[] rows)
        {
            var list = new List<ClashGrouperDialog.TestRow>();
            for (var i = 0; i < rows.Length; i++)
                list.Add(new ClashGrouperDialog.TestRow
                {
                    Index = i,
                    Name = rows[i].name,
                    Total = rows[i].total,
                    Checked = rows[i].on,
                });
            return list;
        }

        private static List<ClashGrouperDialog.RuleChoice> Rules() =>
            new List<ClashGrouperDialog.RuleChoice>
            {
                new ClashGrouperDialog.RuleChoice { Id = "item", Label = "Root-cause element" },
                new ClashGrouperDialog.RuleChoice { Id = "proximity", Label = "Proximity cluster" },
                new ClashGrouperDialog.RuleChoice { Id = "level", Label = "Nearest level" },
            };

        private static Preview PreviewWith(int clashes, int groups)
        {
            var preview = new Preview { TotalClashes = clashes, TotalGroups = groups };
            for (var i = 0; i < groups; i++)
                preview.Groups.Add(new ClashGrouperDialog.PreviewGroup { Name = "G" + i, Count = i + 1 });
            return preview;
        }

        // A preview provider that ignores progress, for the tests that only
        // care about the result.
        private static Func<Config, ClashGrouperDialog.PreviewProgress, Preview> Static(
            int clashes, int groups) => (_, __) => PreviewWith(clashes, groups);

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

        [Fact]
        public void Builds_WithTestRows_SmartDefault_AndStatPreview()
        {
            OnSta(() =>
            {
                Config seen = null;
                var window = ClashGrouperDialog.Build(
                    Tests(("HVAC vs STR", 412, true), ("PLB vs ELE", 156, false)), Rules(),
                    (config, __) => { seen = config; return PreviewWith(412, 23); });

                ClashGrouperDialog.RunPreviewForTest(window);

                Assert.Equal(2, ClashGrouperDialog.TestCheckCountOf(window));
                Assert.True(ClashGrouperDialog.IsSmartOf(window));
                Assert.Equal("412>23", ClashGrouperDialog.StatTextOf(window));
                Assert.Equal(23, ClashGrouperDialog.PreviewRowCountOf(window));
                Assert.True(ClashGrouperDialog.ApplyEnabledOf(window));
                Assert.NotNull(seen);
                Assert.Equal(new List<int> { 0 }, seen.TestIndexes);
                Assert.True(seen.Smart);
                Assert.True(seen.KeepExisting);
            });
        }

        [Fact]
        public void NoTestsTicked_DisablesApply_AndPointsAtTheList()
        {
            OnSta(() =>
            {
                var window = ClashGrouperDialog.Build(
                    Tests(("A vs B", 10, false)), Rules(), Static(10, 2));

                Assert.False(ClashGrouperDialog.ApplyEnabledOf(window));
                Assert.Contains("Choose a clash test", ClashGrouperDialog.MessageOf(window));

                ClashGrouperDialog.SetTestCheckedForTest(window, 0, true);
                ClashGrouperDialog.RunPreviewForTest(window);
                Assert.True(ClashGrouperDialog.ApplyEnabledOf(window));
            });
        }

        [Fact]
        public void CustomMode_CapturesChainedRuleIds_InOrder()
        {
            OnSta(() =>
            {
                var window = ClashGrouperDialog.Build(
                    Tests(("A vs B", 10, true)), Rules(), Static(10, 2));

                ClashGrouperDialog.SetSmartForTest(window, false);
                ClashGrouperDialog.AddRuleRowForTest(window);
                Assert.Equal(2, ClashGrouperDialog.RuleRowCountOf(window));

                var config = ClashGrouperDialog.ConfigOf(window);
                Assert.False(config.Smart);
                // Row n defaults to choice n: item then proximity.
                Assert.Equal(new List<string> { "item", "proximity" }, config.RuleIds);
            });
        }

        [Fact]
        public void Defaults_SeedMode_Tolerance_KeepExisting_AndRuleChain()
        {
            OnSta(() =>
            {
                var defaults = new Config
                {
                    Smart = false,
                    ToleranceMeters = 3.5,
                    KeepExisting = false,
                };
                defaults.RuleIds.Add("level");
                defaults.RuleIds.Add("proximity");

                var window = ClashGrouperDialog.Build(
                    Tests(("A vs B", 10, true)), Rules(), Static(10, 2), defaults: defaults);

                Assert.False(ClashGrouperDialog.IsSmartOf(window));
                Assert.Equal(2, ClashGrouperDialog.RuleRowCountOf(window));

                var config = ClashGrouperDialog.ConfigOf(window);
                Assert.Equal(new List<string> { "level", "proximity" }, config.RuleIds);
                Assert.Equal(3.5, config.ToleranceMeters, 3);
                Assert.False(config.KeepExisting);
            });
        }

        [Fact]
        public void NullDefaults_KeepTheShippedBehavior()
        {
            OnSta(() =>
            {
                var window = ClashGrouperDialog.Build(
                    Tests(("A vs B", 10, true)), Rules(), Static(10, 2), defaults: null);

                Assert.True(ClashGrouperDialog.IsSmartOf(window));
                Assert.Equal(1, ClashGrouperDialog.RuleRowCountOf(window));

                var config = ClashGrouperDialog.ConfigOf(window);
                Assert.Equal(2.0, config.ToleranceMeters, 3);
                Assert.True(config.KeepExisting);
            });
        }

        [Fact]
        public void PreviewException_ShowsFailure_InsteadOfCrashing()
        {
            OnSta(() =>
            {
                var window = ClashGrouperDialog.Build(
                    Tests(("A vs B", 10, true)), Rules(),
                    (_, __) => throw new InvalidOperationException("engine says no"));

                ClashGrouperDialog.RunPreviewForTest(window);

                Assert.Contains("Preview did not load", ClashGrouperDialog.MessageOf(window));
                Assert.False(ClashGrouperDialog.ApplyEnabledOf(window));
            });
        }

        [Fact]
        public void PreviewException_ShowsTheReason_InAVisibleElement()
        {
            OnSta(() =>
            {
                var window = ClashGrouperDialog.Build(
                    Tests(("A vs B", 10, true)), Rules(),
                    (_, __) => throw new InvalidOperationException("engine says no"));

                ClashGrouperDialog.RunPreviewForTest(window);

                Assert.Equal("Failed", ClashGrouperDialog.PreviewStateOf(window));
                // "Preview did not load." on its own reads as if the dialog has
                // nothing more to say, so the reason has to be on screen too,
                // not parked in a collapsed panel.
                Assert.Contains("engine says no", ClashGrouperDialog.VisibleStaleTextOf(window));
            });
        }

        [Fact]
        public void DuringPreview_TheFooterCancelIsLocked_ButTheProgressCancelIsNot()
        {
            OnSta(() =>
            {
                var footerDuring = true;
                var progressDuring = false;
                var window = (Window)null;
                window = ClashGrouperDialog.Build(
                    Tests(("A vs B", 1000, true)), Rules(),
                    (_, report) =>
                    {
                        report(0.5, "Reading clashes");
                        // Report pumps the dispatcher, so a click on the footer
                        // Cancel really could land here and close the window
                        // out from under this call.
                        footerDuring = ClashGrouperDialog.DialogCancelEnabledOf(window);
                        progressDuring = ClashGrouperDialog.PreviewCancelEnabledOf(window);
                        return PreviewWith(1000, 3);
                    });

                ClashGrouperDialog.RunPreviewForTest(window);

                Assert.False(footerDuring);
                Assert.True(progressDuring);
                // ...and it comes back afterwards.
                Assert.True(ClashGrouperDialog.DialogCancelEnabledOf(window));
            });
        }

        [Fact]
        public void ZeroGroupsProduced_DisablesApply_WithGuidance()
        {
            OnSta(() =>
            {
                var window = ClashGrouperDialog.Build(
                    Tests(("A vs B", 10, true)), Rules(), Static(10, 0));

                ClashGrouperDialog.RunPreviewForTest(window);

                Assert.False(ClashGrouperDialog.ApplyEnabledOf(window));
                Assert.Contains("No groups to make", ClashGrouperDialog.MessageOf(window));
            });
        }

        [Fact]
        public void Sections_CollapseByDefault_AndAutoExpandWhenTheyNeedAttention()
        {
            OnSta(() =>
            {
                // Happy path: something is ticked, Smart is on - both sections
                // stay collapsed and the preview carries the dialog.
                var quiet = ClashGrouperDialog.Build(
                    Tests(("A vs B", 10, true)), Rules(), Static(10, 2));
                Assert.False(ClashGrouperDialog.TestsSectionOpenOf(quiet));
                Assert.False(ClashGrouperDialog.ModeSectionOpenOf(quiet));

                // Nothing ticked: the tests section opens itself.
                var needy = ClashGrouperDialog.Build(
                    Tests(("A vs B", 10, false)), Rules(), Static(10, 2));
                Assert.True(ClashGrouperDialog.TestsSectionOpenOf(needy));
            });
        }

        [Fact]
        public void LongPreview_IsCapped_WithAnOverflowRow()
        {
            OnSta(() =>
            {
                var window = ClashGrouperDialog.Build(
                    Tests(("A vs B", 1000, true)), Rules(), Static(1000, 100));

                ClashGrouperDialog.RunPreviewForTest(window);

                // 60 rows + 1 "not shown here" trailer.
                Assert.Equal(61, ClashGrouperDialog.PreviewRowCountOf(window));
            });
        }

        [Fact]
        public void Build_RunsNoPreview_AndOffersTheButtonInstead()
        {
            OnSta(() =>
            {
                var calls = 0;
                var window = ClashGrouperDialog.Build(
                    Tests(("A vs B", 67000, true)), Rules(),
                    (_, __) => { calls++; return PreviewWith(67000, 12); });

                // The whole point: opening the dialog reads nothing.
                Assert.Equal(0, calls);
                Assert.Equal("Ready", ClashGrouperDialog.PreviewStateOf(window));
                Assert.False(ClashGrouperDialog.ApplyEnabledOf(window));
                Assert.Contains("67,000", ClashGrouperDialog.SelectionSummaryOf(window));
            });
        }

        [Fact]
        public void PreviewButton_FillsTheReadout_AndEnablesApply()
        {
            OnSta(() =>
            {
                var window = ClashGrouperDialog.Build(
                    Tests(("A vs B", 100, true)), Rules(), Static(100, 4));

                ClashGrouperDialog.RunPreviewForTest(window);

                Assert.Equal("Done", ClashGrouperDialog.PreviewStateOf(window));
                Assert.Equal("100>4", ClashGrouperDialog.StatTextOf(window));
                Assert.True(ClashGrouperDialog.ApplyEnabledOf(window));
            });
        }

        [Fact]
        public void ChangingSettings_AfterAPreview_GoesStale_AndDisablesApply()
        {
            OnSta(() =>
            {
                var window = ClashGrouperDialog.Build(
                    Tests(("A vs B", 100, true)), Rules(), Static(100, 4));
                ClashGrouperDialog.RunPreviewForTest(window);
                Assert.True(ClashGrouperDialog.ApplyEnabledOf(window));

                ClashGrouperDialog.SetSmartForTest(window, false);

                Assert.Equal("Stale", ClashGrouperDialog.PreviewStateOf(window));
                Assert.False(ClashGrouperDialog.ApplyEnabledOf(window));
                Assert.Contains("Settings changed", ClashGrouperDialog.StaleNoteOf(window));
                // The old numbers stay on screen; they are still true of the
                // settings that produced them.
                Assert.Equal("100>4", ClashGrouperDialog.StatTextOf(window));
            });
        }

        [Fact]
        public void PreviewReportsProgress_AndTheDialogAcceptsIt()
        {
            OnSta(() =>
            {
                var stages = new List<string>();
                var kept = true;
                var window = ClashGrouperDialog.Build(
                    Tests(("A vs B", 1000, true)), Rules(),
                    (_, report) =>
                    {
                        kept &= report(0.25, "Reading clashes");
                        stages.Add("Reading clashes");
                        kept &= report(1.0, "Grouping");
                        stages.Add("Grouping");
                        return PreviewWith(1000, 7);
                    });

                ClashGrouperDialog.RunPreviewForTest(window);

                Assert.True(kept);   // nobody cancelled, so report stays true
                Assert.Equal(new[] { "Reading clashes", "Grouping" }, stages);
                Assert.Equal("Done", ClashGrouperDialog.PreviewStateOf(window));
            });
        }

        [Fact]
        public void CancelDuringPreview_TurnsReportFalse_AndReturnsToReady()
        {
            OnSta(() =>
            {
                var window = (Window)null;
                var secondReport = true;
                window = ClashGrouperDialog.Build(
                    Tests(("A vs B", 1000, true)), Rules(),
                    (_, report) =>
                    {
                        report(0.1, "Reading clashes");
                        ClashGrouperDialog.RequestCancelForTest(window);
                        secondReport = report(0.2, "Reading clashes");
                        return null;   // the python side returns None when cancelled
                    });

                ClashGrouperDialog.RunPreviewForTest(window);

                Assert.False(secondReport);
                Assert.Equal("Ready", ClashGrouperDialog.PreviewStateOf(window));
                Assert.False(ClashGrouperDialog.ApplyEnabledOf(window));
                // The view, not just the model: a note the user cannot read is
                // not a note. Same StaleText/ActionPanel pair as the Failed state.
                Assert.Contains("cancelled", ClashGrouperDialog.VisibleStaleTextOf(window));
            });
        }

        [Fact]
        public void ClosingTheWindowDuringPreview_IsVetoed_AndBecomesACancel()
        {
            OnSta(() =>
            {
                var window = (Window)null;
                var vetoed = false;
                var reportAfterClose = true;
                window = ClashGrouperDialog.Build(
                    Tests(("A vs B", 1000, true)), Rules(),
                    (_, report) =>
                    {
                        report(0.1, "Reading clashes");
                        // The X button and Alt+F4 arrive as WM_CLOSE through the
                        // dispatcher pump; SetInteractive cannot disable native
                        // chrome, so Closing has to hold the line.
                        window.Close();
                        reportAfterClose = report(0.2, "Reading clashes");
                        return null;
                    });
                // Runs after the dialog's own handler, so it observes the veto.
                window.Closing += (s, e) => vetoed = e.Cancel;

                ClashGrouperDialog.RunPreviewForTest(window);

                Assert.True(vetoed);
                Assert.False(reportAfterClose);
                Assert.Equal("Ready", ClashGrouperDialog.PreviewStateOf(window));
                Assert.Contains("cancelled", ClashGrouperDialog.VisibleStaleTextOf(window));
            });
        }

        [Fact]
        public void ToleranceEdits_DoNotStaleAChainThatIgnoresThem()
        {
            OnSta(() =>
            {
                // Custom chain, default first rule "item": nothing clusters, so
                // the tolerance cannot change the plan.
                var window = ClashGrouperDialog.Build(
                    Tests(("A vs B", 100, true)), Rules(), Static(100, 4));
                ClashGrouperDialog.SetSmartForTest(window, false);
                ClashGrouperDialog.RunPreviewForTest(window);
                Assert.Equal("Done", ClashGrouperDialog.PreviewStateOf(window));

                ClashGrouperDialog.SetToleranceForTest(window, "7.5");

                Assert.Equal("Done", ClashGrouperDialog.PreviewStateOf(window));
                Assert.True(ClashGrouperDialog.ApplyEnabledOf(window));

                // Smart does cluster, so there the same keystroke must stale it -
                // otherwise this test would also pass with tolerance left out of
                // the signature entirely.
                var smart = ClashGrouperDialog.Build(
                    Tests(("A vs B", 100, true)), Rules(), Static(100, 4));
                ClashGrouperDialog.RunPreviewForTest(smart);
                Assert.Equal("Done", ClashGrouperDialog.PreviewStateOf(smart));

                ClashGrouperDialog.SetToleranceForTest(smart, "7.5");

                Assert.Equal("Stale", ClashGrouperDialog.PreviewStateOf(smart));
                Assert.False(ClashGrouperDialog.ApplyEnabledOf(smart));
            });
        }

        [Fact]
        public void RunPreview_IsNotReentrant_WhileOneIsAlreadyRunning()
        {
            OnSta(() =>
            {
                var window = (Window)null;
                var calls = 0;
                window = ClashGrouperDialog.Build(
                    Tests(("A vs B", 100, true)), Rules(),
                    (_, __) =>
                    {
                        calls++;
                        // A pumped click on a live control must not start a
                        // second read on top of this one.
                        ClashGrouperDialog.RunPreviewForTest(window);
                        return PreviewWith(100, 3);
                    });

                ClashGrouperDialog.RunPreviewForTest(window);

                Assert.Equal(1, calls);
                Assert.Equal("Done", ClashGrouperDialog.PreviewStateOf(window));
            });
        }

        [Fact]
        public void StaleNote_IsClearedOnceTheResultIsCurrentAgain()
        {
            OnSta(() =>
            {
                // Zero groups is the Done path that still calls ShowAction, so a
                // note left over from Stale would be painted over a live result.
                var window = ClashGrouperDialog.Build(
                    Tests(("A vs B", 100, true)), Rules(), Static(100, 0));
                ClashGrouperDialog.RunPreviewForTest(window);

                ClashGrouperDialog.SetToleranceForTest(window, "7.5");
                Assert.Equal("Stale", ClashGrouperDialog.PreviewStateOf(window));
                Assert.Contains("Settings changed", ClashGrouperDialog.VisibleStaleTextOf(window));

                ClashGrouperDialog.SetToleranceForTest(window, "2.0");

                Assert.Equal("Done", ClashGrouperDialog.PreviewStateOf(window));
                Assert.Equal("", ClashGrouperDialog.StaleNoteOf(window));
                Assert.Equal("", ClashGrouperDialog.VisibleStaleTextOf(window));
            });
        }

        [Fact]
        public void FailureNote_DoesNotSurvive_ARoundTripThroughNoSelection()
        {
            OnSta(() =>
            {
                var window = ClashGrouperDialog.Build(
                    Tests(("A vs B", 100, true)), Rules(),
                    (_, __) => throw new InvalidOperationException("engine says no"));
                ClashGrouperDialog.RunPreviewForTest(window);
                Assert.Equal("Failed", ClashGrouperDialog.PreviewStateOf(window));
                Assert.Contains("engine says no", ClashGrouperDialog.VisibleStaleTextOf(window));

                // Unticking routes through NeedsSelection, which leaves the
                // Failed check in RefreshPreview permanently behind, so re-ticking
                // lands in Ready with a note that no longer explains anything.
                ClashGrouperDialog.SetTestCheckedForTest(window, 0, false);
                ClashGrouperDialog.SetTestCheckedForTest(window, 0, true);

                Assert.Equal("Ready", ClashGrouperDialog.PreviewStateOf(window));
                Assert.Equal("", ClashGrouperDialog.VisibleStaleTextOf(window));
                Assert.Equal("", ClashGrouperDialog.StaleNoteOf(window));
            });
        }

        // The throttle in Report gates on Environment.TickCount, which is signed
        // 32-bit and spends half of every ~49.7 day cycle negative. Two real
        // samples subtract correctly straight across the wrap; a sentinel does
        // not, and seeding LastPump with 0 froze the dialog on any machine up
        // more than ~24.9 days. Pure arithmetic, so it is tested as arithmetic.
        [Theory]
        // The exact case that was broken: a 0 sentinel in each half-cycle.
        [InlineData(int.MaxValue, 0, true)]      // positive half: passed by luck
        [InlineData(int.MinValue, 0, false)]     // negative half: pumping died here
        [InlineData(int.MinValue + 5000, 0, false)]
        // Two genuine samples straddling the MaxValue -> MinValue rollover.
        [InlineData(int.MinValue + 39, int.MaxValue, true)]    // exactly 40ms elapsed
        [InlineData(int.MinValue + 38, int.MaxValue, false)]   // 39ms, not yet
        [InlineData(int.MinValue + 1039, int.MaxValue, true)]
        // Ordinary operation in each half-cycle.
        [InlineData(1040, 1000, true)]
        [InlineData(1039, 1000, false)]
        [InlineData(1000, 1000, false)]
        [InlineData(int.MinValue + 100, int.MinValue + 50, true)]
        [InlineData(int.MinValue + 89, int.MinValue + 50, false)]
        public void PumpThrottle_MeasuresElapsedAcrossTheTickCountRollover(
            int now, int lastPump, bool due)
        {
            Assert.Equal(due, ClashGrouperDialog.PumpDueForTest(now, lastPump));
        }

        [Fact]
        public void Counts_ArriveAfterTheWindowOpens_AndPreselectTestsWithLooseClashes()
        {
            OnSta(() =>
            {
                var asked = new List<int>();
                var window = ClashGrouperDialog.Build(
                    Tests(("Loose", 0, false), ("Fully grouped", 0, false)),
                    Rules(), Static(100, 2),
                    index =>
                    {
                        asked.Add(index);
                        return index == 0
                            ? new ClashGrouperDialog.TestCounts { Total = 500, Grouped = 100 }
                            : new ClashGrouperDialog.TestCounts { Total = 40, Grouped = 40 };
                    });

                // Nothing is counted while the window is being built.
                Assert.Empty(asked);

                ClashGrouperDialog.FillCountsForTest(window);

                Assert.Equal(new[] { 0, 1 }, asked);
                // A test with loose clashes ticks itself; a fully grouped one
                // has nothing to do, so it stays off.
                Assert.Equal(new[] { 0 }, ClashGrouperDialog.ConfigOf(window).TestIndexes.ToArray());
                Assert.Contains("500", ClashGrouperDialog.SelectionSummaryOf(window));
            });
        }

        // The field report's own names: long, and sharing a prefix, so typing a
        // fragment is the only fast way to reach a subset.
        private static (string, int, bool)[] FieldReportTests() => new[]
        {
            ("RMM-MDUCT", 900, false),
            ("RMM-MDUCT-RMM-MEQPM", 400, false),
            ("RMM-MDUCT-ACC-PMAIN", 700, false),
            ("RMM-MDUCT-ACC-MPPIPE", 100, false),
            ("RMM-MDUCT-LEA-EMAIN", 300, false),
            ("RMM-MDUCT-PPE-IMAIN", 500, false),
        };

        [Fact]
        public void ArrivingCounts_DoNotReParentTheRows()
        {
            OnSta(() =>
            {
                var window = ClashGrouperDialog.Build(
                    Tests(FieldReportTests()), Rules(), Static(100, 2),
                    index => new ClashGrouperDialog.TestCounts { Total = 100 + index, Grouped = 0 });

                var before = ClashGrouperDialog.TestListRenderCountOf(window);
                ClashGrouperDialog.FillCountsForTest(window);

                // Every arriving count runs Refresh. In name order the visible
                // sequence has not changed, so pulling 54 Buttons out of the
                // panel and putting them straight back is pure waste.
                Assert.Equal(before, ClashGrouperDialog.TestListRenderCountOf(window));

                // A filter is a real change, so that one does re-parent.
                ClashGrouperDialog.FilterTestsForTest(window, "ACC");
                Assert.Equal(before + 1, ClashGrouperDialog.TestListRenderCountOf(window));
                // ...and typing more text that narrows to the same rows does not.
                ClashGrouperDialog.FilterTestsForTest(window, "acc-");
                Assert.Equal(before + 1, ClashGrouperDialog.TestListRenderCountOf(window));
            });
        }

        /// <summary>Forces a real measure/arrange on a window that is never
        /// shown, so a ScrollViewer inside it has a genuine extent and a
        /// meaningful VerticalOffset.</summary>
        private static void LayOut(Window window)
        {
            // The window's own Measure does nothing useful before it is shown
            // (no HwndSource), so lay out its content element directly.
            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(window.Width, window.Height));
            content.Arrange(new Rect(0, 0, window.Width, window.Height));
            content.UpdateLayout();
        }

        [Fact]
        public void ReParentingTheRows_HoldsTheScrollPosition()
        {
            OnSta(() =>
            {
                // 54 rows, the field report's count, so the list genuinely
                // scrolls: ascending counts mean the count sort reverses it,
                // which is a re-parent that cannot be skipped.
                var rows = new (string, int, bool)[54];
                for (var i = 0; i < rows.Length; i++)
                    rows[i] = ("RMM-MDUCT-" + i.ToString("00"), 100 + i, false);
                var window = ClashGrouperDialog.Build(Tests(rows), Rules(), Static(100, 2));
                LayOut(window);

                ClashGrouperDialog.ScrollTestListForTest(window, 400);
                LayOut(window);
                var before = ClashGrouperDialog.TestListScrollOf(window);
                Assert.True(before > 0, "the list did not scroll, so this proves nothing");

                var renders = ClashGrouperDialog.TestListRenderCountOf(window);
                ClashGrouperDialog.ToggleSortForTest(window);
                LayOut(window);

                // It really did re-parent...
                Assert.Equal(renders + 1, ClashGrouperDialog.TestListRenderCountOf(window));
                // ...and the user is still looking at the same place in the list.
                // This pins the behaviour, not the mechanism: WPF happens to
                // satisfy it on its own today (verified: the test passes with
                // RefreshTestList's ScrollToVerticalOffset removed), and the call
                // is there so it keeps holding if that ever stops being true.
                Assert.Equal(before, ClashGrouperDialog.TestListScrollOf(window));
            });
        }

        [Fact]
        public void TheRowReadsTotalFirst_ThenTheGroupedCount()
        {
            OnSta(() =>
            {
                var window = ClashGrouperDialog.Build(
                    Tests(("HVAC vs STR", 0, false)), Rules(), Static(100, 2),
                    _ => new ClashGrouperDialog.TestCounts { Total = 50436, Grouped = 25412 });

                ClashGrouperDialog.FillCountsForTest(window);

                // The total is the primary number, so it comes first. Both cells
                // are docked Right, where the first child added is the rightmost,
                // so getting this wrong is a one-line ordering mistake with no
                // other symptom.
                Assert.Equal(new[] { "50,436", "25,412 grouped" },
                    ClashGrouperDialog.TestNumbersInReadingOrderOf(window, 0));
            });
        }

        [Fact]
        public void DuringPreview_TheFilterAndBulkButtons_AreLockedToo()
        {
            OnSta(() =>
            {
                var during = true;
                var window = (Window)null;
                window = ClashGrouperDialog.Build(
                    Tests(("A vs B", 1000, true)), Rules(),
                    (_, report) =>
                    {
                        // Report pumps the dispatcher, so a keystroke in the
                        // filter box or a click on All really can land here, and
                        // Refresh is inert while a preview runs: the toolbar has
                        // to be disabled, not merely ignored.
                        report(0.5, "Reading clashes");
                        during = ClashGrouperDialog.FilterEnabledOf(window);
                        return PreviewWith(1000, 3);
                    });

                ClashGrouperDialog.RunPreviewForTest(window);

                Assert.False(during);
                Assert.True(ClashGrouperDialog.FilterEnabledOf(window));
            });
        }

        [Fact]
        public void TheTestList_GetsTheHeightThePreviewPanelWasWasting()
        {
            OnSta(() =>
            {
                var window = ClashGrouperDialog.Build(
                    Tests(FieldReportTests()), Rules(), Static(100, 2));

                // Rows are 32px. The old constant 200 showed six of 54, which is
                // what made the list unusable; the default size owes about 11.
                Assert.True(ClashGrouperDialog.TestListMaxHeightOf(window) >= 11 * 32,
                    "list max height was " + ClashGrouperDialog.TestListMaxHeightOf(window));
            });
        }

        [Fact]
        public void Filtering_NarrowsTheVisibleRows_ButNeverTheSelection()
        {
            OnSta(() =>
            {
                var window = ClashGrouperDialog.Build(
                    Tests(FieldReportTests()), Rules(), Static(100, 2));

                // Tick a row that the filter is about to hide.
                ClashGrouperDialog.SetTestCheckedForTest(window, 0, true);
                ClashGrouperDialog.FilterTestsForTest(window, "acc");   // case-insensitive

                Assert.Equal(
                    new[] { "RMM-MDUCT-ACC-PMAIN", "RMM-MDUCT-ACC-MPPIPE" },
                    ClashGrouperDialog.VisibleTestNamesOf(window));
                // Hiding is not deselecting: the row keeps its tick and the
                // header summary keeps reporting the truth across all 6.
                Assert.True(ClashGrouperDialog.TestCheckedOf(window, 0));
                Assert.Equal(new[] { 0 }, ClashGrouperDialog.ConfigOf(window).TestIndexes.ToArray());
                Assert.Equal("1 of 6 selected", ClashGrouperDialog.TestsSummaryOf(window));
                Assert.Contains("Showing 2 of 6", ClashGrouperDialog.FilterNoteOf(window));

                // Clearing the box shows everything again, with no note.
                ClashGrouperDialog.FilterTestsForTest(window, "");
                Assert.Equal(6, ClashGrouperDialog.VisibleTestNamesOf(window).Length);
                Assert.Equal("", ClashGrouperDialog.FilterNoteOf(window));
            });
        }

        [Fact]
        public void AllAndNone_ActOnTheFilteredSubset_AndCountAsAChoice()
        {
            OnSta(() =>
            {
                var window = ClashGrouperDialog.Build(
                    Tests(FieldReportTests()), Rules(), Static(100, 2));

                ClashGrouperDialog.FilterTestsForTest(window, "ACC");
                ClashGrouperDialog.SelectAllVisibleForTest(window);

                // Rows 2 and 3 are the ACC pair; nothing else moved.
                Assert.Equal(new[] { 2, 3 }, ClashGrouperDialog.ConfigOf(window).TestIndexes.ToArray());
                Assert.True(ClashGrouperDialog.SelectionTouchedOf(window));

                // Widen the filter, tick everything, then narrow and clear: None
                // must leave the rows it cannot see alone.
                ClashGrouperDialog.FilterTestsForTest(window, "");
                ClashGrouperDialog.SelectAllVisibleForTest(window);
                Assert.Equal(6, ClashGrouperDialog.ConfigOf(window).TestIndexes.Count);

                ClashGrouperDialog.FilterTestsForTest(window, "ACC");
                ClashGrouperDialog.SelectNoneVisibleForTest(window);
                Assert.Equal(
                    new[] { 0, 1, 4, 5 },
                    ClashGrouperDialog.ConfigOf(window).TestIndexes.ToArray());
            });
        }

        [Fact]
        public void SortingByCount_ReordersTheVisibleRows_NotTheUnderlyingList()
        {
            OnSta(() =>
            {
                var window = ClashGrouperDialog.Build(
                    Tests(FieldReportTests()), Rules(), Static(100, 2),
                    index => new ClashGrouperDialog.TestCounts
                    {
                        Total = Tests(FieldReportTests())[index].Total,
                        Grouped = 0,
                    });
                ClashGrouperDialog.FillCountsForTest(window);

                Assert.Equal("Sort: name", ClashGrouperDialog.SortLabelOf(window));
                ClashGrouperDialog.ToggleSortForTest(window);
                Assert.Equal("Sort: count", ClashGrouperDialog.SortLabelOf(window));

                Assert.Equal(new[]
                {
                    "RMM-MDUCT",             // 900
                    "RMM-MDUCT-ACC-PMAIN",   // 700
                    "RMM-MDUCT-PPE-IMAIN",   // 500
                    "RMM-MDUCT-RMM-MEQPM",   // 400
                    "RMM-MDUCT-LEA-EMAIN",   // 300
                    "RMM-MDUCT-ACC-MPPIPE",  // 100
                }, ClashGrouperDialog.VisibleTestNamesOf(window));

                // TestRows itself must not have moved: every positional reader
                // (PaintTestCounts, SetTestCheckedForTest, CountOne) indexes it
                // against parts.Tests. Row 3 is still the 100-clash test.
                Assert.Equal("100", ClashGrouperDialog.TestCountTextOf(window, 3));
                Assert.Equal("900", ClashGrouperDialog.TestCountTextOf(window, 0));
                ClashGrouperDialog.SetTestCheckedForTest(window, 3, true);
                Assert.Equal(new[] { 3 }, ClashGrouperDialog.ConfigOf(window).TestIndexes.ToArray());

                // Back to name order, which is the order walk_tests yielded.
                ClashGrouperDialog.ToggleSortForTest(window);
                Assert.Equal("RMM-MDUCT-RMM-MEQPM",
                    ClashGrouperDialog.VisibleTestNamesOf(window)[1]);
            });
        }

        [Fact]
        public void UncountedRows_SortLast_BecauseTheirNumberIsUnknown()
        {
            OnSta(() =>
            {
                // Total -1 is "not counted yet" and -2 is "count failed". Both
                // are negative, so a naive descending sort would file them below
                // a genuine zero rather than at the end.
                var window = ClashGrouperDialog.Build(
                    Tests(("Uncounted", -1, false), ("Small", 5, false),
                          ("Big", 900, false), ("Zero", 0, false)),
                    Rules(), Static(100, 2));

                ClashGrouperDialog.ToggleSortForTest(window);

                Assert.Equal(new[] { "Big", "Small", "Zero", "Uncounted" },
                    ClashGrouperDialog.VisibleTestNamesOf(window));

                // The other sentinel, reached the way the dialog really reaches
                // it: a count provider that throws leaves Total at -2, which is
                // further from zero than -1 and would sort even more wrongly.
                var failed = ClashGrouperDialog.Build(
                    Tests(("Broken", 0, false), ("Zero", 0, false), ("Big", 0, false)),
                    Rules(), Static(100, 2),
                    index => index == 0
                        ? throw new InvalidOperationException("no results")
                        : new ClashGrouperDialog.TestCounts
                        {
                            Total = index == 1 ? 0 : 900,
                            Grouped = 0,
                        });
                ClashGrouperDialog.FillCountsForTest(failed);
                Assert.Equal("!", ClashGrouperDialog.TestCountTextOf(failed, 0));   // -2 landed

                ClashGrouperDialog.ToggleSortForTest(failed);

                Assert.Equal(new[] { "Big", "Zero", "Broken" },
                    ClashGrouperDialog.VisibleTestNamesOf(failed));

                // The case that actually needs the sentinels normalised. Both of
                // the assertions above also hold for a naive sort on raw Total,
                // because -1 and -2 are already below every real count. What a
                // naive sort gets wrong is ordering the two sentinels against
                // each other: -2 would sort below -1, so a failed test would be
                // filed under an uncounted one for no reason a user could
                // explain. Mapping both to one key and falling back to position
                // keeps them in the order walk_tests yielded.
                var mixed = ClashGrouperDialog.Build(
                    Tests(("Broken", -2, false),      // CountFailedSentinel
                          ("Uncounted", -1, false),
                          ("Big", 900, false)),
                    Rules(), Static(100, 2));

                ClashGrouperDialog.ToggleSortForTest(mixed);

                Assert.Equal(new[] { "Big", "Broken", "Uncounted" },
                    ClashGrouperDialog.VisibleTestNamesOf(mixed));
            });
        }

        [Fact]
        public void AFilterMatchingNothing_LeavesTheDialogUsable()
        {
            OnSta(() =>
            {
                var window = ClashGrouperDialog.Build(
                    Tests(FieldReportTests()), Rules(), Static(100, 2));
                ClashGrouperDialog.SetTestCheckedForTest(window, 0, true);

                ClashGrouperDialog.FilterTestsForTest(window, "zzz");

                Assert.Empty(ClashGrouperDialog.VisibleTestNamesOf(window));
                Assert.Contains("Showing 0 of 6", ClashGrouperDialog.FilterNoteOf(window));
                // Bulk buttons over an empty set are no-ops, not crashes, and
                // sorting an empty set is fine too.
                ClashGrouperDialog.SelectAllVisibleForTest(window);
                ClashGrouperDialog.SelectNoneVisibleForTest(window);
                ClashGrouperDialog.ToggleSortForTest(window);
                Assert.Equal(new[] { 0 }, ClashGrouperDialog.ConfigOf(window).TestIndexes.ToArray());

                // And the rows come back intact.
                ClashGrouperDialog.FilterTestsForTest(window, "");
                Assert.Equal(6, ClashGrouperDialog.VisibleTestNamesOf(window).Length);
                Assert.True(ClashGrouperDialog.TestCheckedOf(window, 0));
            });
        }

        [Fact]
        public void AFilterHidingTickedTests_SaysSo_SoThePreviewIsNoSurprise()
        {
            OnSta(() =>
            {
                var window = ClashGrouperDialog.Build(
                    Tests(FieldReportTests()), Rules(), Static(100, 2));
                ClashGrouperDialog.SetTestCheckedForTest(window, 0, true);
                ClashGrouperDialog.SetTestCheckedForTest(window, 1, true);

                ClashGrouperDialog.FilterTestsForTest(window, "ACC");

                // Two ticked tests are off screen and would still be previewed,
                // so the filter line has to own up to them.
                Assert.Contains("2 selected are hidden", ClashGrouperDialog.FilterNoteOf(window));
                // The line right beside the Preview button keeps the true total.
                Assert.Contains("2 tests", ClashGrouperDialog.SelectionSummaryOf(window));
            });
        }

        [Fact]
        public void OnAHandfulOfTests_TheLooseClashAutoTickSurvives_AtTheBoundary()
        {
            OnSta(() =>
            {
                // Exactly at the ceiling: still the old convenience.
                var rows = new (string, int, bool)[5];
                for (var i = 0; i < rows.Length; i++) rows[i] = ("T" + i, 0, false);
                var window = ClashGrouperDialog.Build(
                    Tests(rows), Rules(), Static(100, 2),
                    index => new ClashGrouperDialog.TestCounts
                    {
                        // Only T1 has loose clashes.
                        Total = 500,
                        Grouped = index == 1 ? 100 : 500,
                    });

                ClashGrouperDialog.FillCountsForTest(window);

                Assert.Equal(new[] { 1 }, ClashGrouperDialog.ConfigOf(window).TestIndexes.ToArray());
            });
        }

        [Fact]
        public void OnADocumentWithManyTests_NothingIsAutoTicked()
        {
            OnSta(() =>
            {
                // The field report: 54 tests, of which the old unconditional
                // auto-tick selected 41 and 606,361 clashes. Past a handful of
                // tests the convenience becomes 41 boxes to untick by hand, so
                // it is off and the user chooses.
                var rows = new (string, int, bool)[6];
                for (var i = 0; i < rows.Length; i++) rows[i] = ("T" + i, 0, false);
                var window = ClashGrouperDialog.Build(
                    Tests(rows), Rules(), Static(100, 2),
                    _ => new ClashGrouperDialog.TestCounts { Total = 500, Grouped = 100 });

                ClashGrouperDialog.FillCountsForTest(window);

                Assert.Empty(ClashGrouperDialog.ConfigOf(window).TestIndexes);
                Assert.Equal("NeedsSelection", ClashGrouperDialog.PreviewStateOf(window));
            });
        }

        [Fact]
        public void ATestThatWillNotCount_DoesNotTakeTheDialogDown()
        {
            OnSta(() =>
            {
                var window = ClashGrouperDialog.Build(
                    Tests(("Broken", 0, false)), Rules(), Static(10, 1),
                    index => throw new InvalidOperationException("no results"));

                ClashGrouperDialog.FillCountsForTest(window);

                Assert.Equal("NeedsSelection", ClashGrouperDialog.PreviewStateOf(window));
                // A failed count must not render like a genuine zero: it gets
                // its own one-character marker and the real reason in a
                // tooltip, and it is never auto-ticked.
                Assert.Equal("!", ClashGrouperDialog.TestCountTextOf(window, 0));
                Assert.Contains("no results", ClashGrouperDialog.TestCountTooltipOf(window, 0));
                Assert.False(ClashGrouperDialog.TestCheckedOf(window, 0));
            });
        }

        [Fact]
        public void AQueuedCount_DrainedFromInsideAPreview_DoesNotRun_AndThePreviewStaysDone()
        {
            OnSta(() =>
            {
                Window window = null;
                var asked = new List<int>();
                window = ClashGrouperDialog.Build(
                    // "Loose" is unticked and will read as loose-clashes-not-
                    // grouped once counted, so if it gets counted mid-preview
                    // it auto-ticks and changes TestIndexes out from under the
                    // preview that is still running. "Ticked" is already
                    // selected so the preview has something to run on and can
                    // reach Done rather than NeedsSelection.
                    Tests(("Loose", 0, false), ("Ticked", 0, true)), Rules(),
                    (config, report) =>
                    {
                        // Mirrors what Report does for a real progress tick: it
                        // pumps the dispatcher at Background priority, which is
                        // exactly the mechanism that can drain a counting
                        // continuation queued before this preview started.
                        report(0.5, "working");
                        return PreviewWith(10, 2);
                    },
                    index =>
                    {
                        asked.Add(index);
                        return new ClashGrouperDialog.TestCounts { Total = 5, Grouped = 0 };
                    });

                // Queue the first counting tick the way ContentRendered would,
                // without letting it run: nothing has pumped the dispatcher yet.
                ClashGrouperDialog.BeginCountingForTest(window);
                Assert.Empty(asked);

                ClashGrouperDialog.RunPreviewForTest(window);

                // The queued tick must have re-paused instead of counting into
                // a live preview: no count call landed, so the row it would
                // have auto-ticked stayed untouched, and the just-finished
                // preview's own selection still matches what it ran with.
                Assert.Empty(asked);
                Assert.Equal("Done", ClashGrouperDialog.PreviewStateOf(window));
            });
        }

        [Fact]
        public void TheCountingPass_ResumingAfterAPreview_LeavesItDone()
        {
            OnSta(() =>
            {
                Window window = null;
                var asked = new List<int>();
                window = ClashGrouperDialog.Build(
                    // "Loose" is unticked and counts as all-loose, so the
                    // resumed pass would auto-tick it, grow TestIndexes, change
                    // the signature and stale the preview the user just waited
                    // through - with no user action at all.
                    Tests(("Ticked", 0, true), ("Loose", 0, false)), Rules(),
                    (_, report) => { report(0.5, "working"); return PreviewWith(10, 2); },
                    index =>
                    {
                        asked.Add(index);
                        return new ClashGrouperDialog.TestCounts { Total = 5, Grouped = 0 };
                    });

                // A counting pass in flight when the user clicks Preview:
                // queued, not yet run.
                ClashGrouperDialog.BeginCountingForTest(window);
                ClashGrouperDialog.RunPreviewForTest(window);
                Assert.Equal("Done", ClashGrouperDialog.PreviewStateOf(window));
                Assert.Empty(asked);   // it re-paused instead of counting mid-preview

                // RunPreview's tail restarted it; now let it actually run, which
                // the sibling test above never does. An Invoke at Background
                // priority drains what is already queued at that priority: one
                // CountOne, plus the tick it re-queues behind the sentinel.
                for (var i = 0; i < 6 && asked.Count < 2; i++)
                    window.Dispatcher.Invoke((Action)(() => { }), DispatcherPriority.Background);

                // The pass finishes and the numbers land...
                Assert.Equal(new[] { 0, 1 }, asked.ToArray());
                Assert.Equal("5", ClashGrouperDialog.TestCountTextOf(window, 1));
                // ...but it must not re-tick anything behind a preview: clicking
                // Preview commits to the selection it ran with.
                Assert.Equal(new[] { 0 }, ClashGrouperDialog.ConfigOf(window).TestIndexes.ToArray());
                Assert.Equal("Done", ClashGrouperDialog.PreviewStateOf(window));
                Assert.True(ClashGrouperDialog.ApplyEnabledOf(window));
            });
        }

        [Fact]
        public void ACancelledRePreview_KeepsTheResult_AndSaysTheCancelLanded()
        {
            OnSta(() =>
            {
                Window window = null;
                var calls = 0;
                window = ClashGrouperDialog.Build(
                    Tests(("A vs B", 100, true)), Rules(),
                    (_, report) =>
                    {
                        calls++;
                        if (calls == 1) return PreviewWith(100, 3);
                        // Second run, same settings: the user hits Cancel, so
                        // the provider stops and answers null.
                        ClashGrouperDialog.RequestCancelForTest(window);
                        report(0.3, "Reading clashes");
                        return null;
                    });

                ClashGrouperDialog.RunPreviewForTest(window);
                Assert.Equal("Done", ClashGrouperDialog.PreviewStateOf(window));

                ClashGrouperDialog.RunPreviewForTest(window);

                // The displayed result is still the one these settings produce,
                // so it stays and Apply stays live...
                Assert.Equal("Done", ClashGrouperDialog.PreviewStateOf(window));
                Assert.True(ClashGrouperDialog.ApplyEnabledOf(window));
                Assert.Equal("100>3", ClashGrouperDialog.StatTextOf(window));
                // ...but the cancel has to be visible somewhere, or the
                // feature's headline control reads as broken.
                Assert.Contains("cancelled", ClashGrouperDialog.ReadoutNoteOf(window));
                // And it must not imply a re-preview is needed, because it is not.
                Assert.DoesNotContain("Settings changed", ClashGrouperDialog.ReadoutNoteOf(window));
            });
        }
    }
}
