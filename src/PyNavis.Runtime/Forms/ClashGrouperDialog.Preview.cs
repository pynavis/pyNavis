using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace PyNavis.Runtime.Forms
{
    /// <summary>
    /// The grouper's preview half: the readout, the group list, and everything
    /// that decides what the panel is currently saying. Split out of
    /// ClashGrouperDialog.cs because that file had grown past the point where
    /// the whole dialog fits in one reading.
    /// </summary>
    public static partial class ClashGrouperDialog
    {
        // ---- preview section ---------------------------------------------------

        private static FrameworkElement BuildPreviewSection(Window window, Parts parts)
        {
            var panel = new DockPanel();
            var header = new StackPanel();

            var numbers = new StackPanel { Orientation = Orientation.Horizontal };
            parts.StatFrom = TabularNumber(parts, "0", 28, parts.Accent);
            parts.StatFrom.FontWeight = FontWeights.Light;
            var arrow = Text(parts, ((char)0x2192).ToString(), 20, parts.T.Muted);
            arrow.VerticalAlignment = VerticalAlignment.Center;
            arrow.Margin = new Thickness(8, 2, 8, 0);
            parts.StatTo = TabularNumber(parts, "0", 28, parts.T.Ink);
            parts.StatTo.FontWeight = FontWeights.Light;
            numbers.Children.Add(parts.StatFrom);
            numbers.Children.Add(arrow);
            numbers.Children.Add(parts.StatTo);

            parts.Subline = Text(parts, "clashes become groups", 12, parts.T.Muted);
            parts.Subline.Margin = new Thickness(0, 2, 0, 0);
            parts.NoteText = Text(parts, "", 12, parts.T.Muted);
            parts.NoteText.Margin = new Thickness(0, 2, 0, 0);

            var readout = new StackPanel();
            readout.Children.Add(numbers);
            readout.Children.Add(parts.Subline);
            readout.Children.Add(parts.NoteText);
            parts.Readout = readout;
            header.Children.Add(readout);

            parts.Message = Text(parts, "", 13, parts.T.Muted);
            parts.Message.Visibility = Visibility.Collapsed;
            header.Children.Add(parts.Message);

            header.Children.Add(BuildActionPanel(window, parts));
            header.Children.Add(BuildProgressPanel(window, parts));

            DockPanel.SetDock(header, Dock.Top);
            panel.Children.Add(header);

            parts.PreviewList = new StackPanel();
            var previewScroll = new ScrollViewer
            {
                Content = parts.PreviewList,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            SlimScroll(previewScroll);
            var container = ListContainer(parts, previewScroll);
            container.Margin = new Thickness(0, 12, 0, 0);
            parts.PreviewShell = container;
            panel.Children.Add(container);
            return panel;
        }

        /// <summary>What the panel shows when there is no current result: how
        /// much is selected, why the last attempt is not on screen, and the one
        /// button that starts the work.</summary>
        private static FrameworkElement BuildActionPanel(Window window, Parts parts)
        {
            parts.SelectionSummary = Text(parts, "", 13, parts.T.Ink);

            parts.StaleText = Text(parts, "", 12, parts.T.Warning);
            parts.StaleText.Margin = new Thickness(0, 6, 0, 0);
            parts.StaleText.TextWrapping = TextWrapping.Wrap;

            var label = Text(parts, "Preview grouping", 13, Colors.White);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            parts.PreviewButton = BareButton(parts, label);
            parts.PreviewButton.Height = 32;
            parts.PreviewButton.MinWidth = 150;
            parts.PreviewButton.Padding = new Thickness(16, 0, 16, 0);
            parts.PreviewButton.Background = Brush(parts.Accent);
            parts.PreviewButton.BorderBrush = Brush(parts.Accent);
            parts.PreviewButton.BorderThickness = new Thickness(1);
            parts.PreviewButton.HorizontalAlignment = HorizontalAlignment.Left;
            parts.PreviewButton.Margin = new Thickness(0, 12, 0, 0);
            parts.PreviewButton.Click += (s, e) => RunPreview(window);

            parts.ActionPanel = new StackPanel();
            parts.ActionPanel.Children.Add(parts.SelectionSummary);
            parts.ActionPanel.Children.Add(parts.StaleText);
            parts.ActionPanel.Children.Add(parts.PreviewButton);
            return parts.ActionPanel;
        }

        /// <summary>What the panel shows while the read is running. The bar is
        /// two borders rather than a themed ProgressBar so it obeys the design
        /// system's palette without a control template.</summary>
        private static FrameworkElement BuildProgressPanel(Window window, Parts parts)
        {
            parts.StageText = Text(parts, "", 13, parts.T.Ink);

            parts.BarFill = new Border
            {
                Background = Brush(parts.Accent),
                CornerRadius = new CornerRadius(3),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 0,
            };
            parts.BarTrack = new Border
            {
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = Brush(parts.T.Surface),
                BorderBrush = Brush(parts.T.Line),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 10, 0, 0),
                Child = parts.BarFill,
            };
            parts.BarTrack.SizeChanged += (s, e) => LayoutBarFill(parts);

            parts.ProgressCount = TabularNumber(parts, "", 12, parts.T.Muted);
            parts.ProgressCount.Margin = new Thickness(0, 6, 0, 0);

            var cancelLabel = Text(parts, "Cancel", 13, parts.T.Ink);
            cancelLabel.HorizontalAlignment = HorizontalAlignment.Center;
            cancelLabel.VerticalAlignment = VerticalAlignment.Center;
            parts.CancelPreview = BareButton(parts, cancelLabel);
            parts.CancelPreview.Height = 32;
            parts.CancelPreview.MinWidth = 84;
            parts.CancelPreview.Background = Brush(parts.T.Paper);
            parts.CancelPreview.BorderBrush = Brush(parts.T.LineStrong);
            parts.CancelPreview.BorderThickness = new Thickness(1);
            parts.CancelPreview.HorizontalAlignment = HorizontalAlignment.Left;
            parts.CancelPreview.Margin = new Thickness(0, 12, 0, 0);
            Hover(parts.CancelPreview, () => parts.T.Paper, parts.T.Surface);
            parts.CancelPreview.Click += (s, e) => parts.CancelRequested = true;

            parts.ProgressPanel = new StackPanel { Visibility = Visibility.Collapsed };
            parts.ProgressPanel.Children.Add(parts.StageText);
            parts.ProgressPanel.Children.Add(parts.BarTrack);
            parts.ProgressPanel.Children.Add(parts.ProgressCount);
            parts.ProgressPanel.Children.Add(parts.CancelPreview);
            return parts.ProgressPanel;
        }

        private static void LayoutBarFill(Parts parts)
        {
            var width = parts.BarTrack.ActualWidth;
            parts.BarFill.Width = width > 0 ? width * parts.BarFraction : 0;
        }

        // ---- running a preview -------------------------------------------------

        /// <summary>Everything about a config that can change the plan. The
        /// tolerance only counts when something actually clusters, so typing in
        /// that box does not invalidate a rule chain that ignores it.</summary>
        private static string SignatureOf(GrouperConfig config)
        {
            var text = new System.Text.StringBuilder();
            text.Append(config.Smart ? "smart" : "custom").Append('|');
            foreach (var id in config.RuleIds) text.Append(id).Append(',');
            text.Append('|');
            var clusters = config.Smart || config.RuleIds.Contains("proximity");
            text.Append(clusters
                ? config.ToleranceMeters.ToString("R", CultureInfo.InvariantCulture)
                : "-");
            text.Append('|').Append(config.KeepExisting ? "keep" : "flat").Append('|');
            foreach (var index in config.TestIndexes) text.Append(index).Append(',');
            return text.ToString();
        }

        private static int SelectedClashCount(Parts parts)
        {
            var total = 0;
            foreach (var row in parts.TestRows)
                if (row.Checked && parts.Tests[row.Index].Total > 0)
                    total += parts.Tests[row.Index].Total;
            return total;
        }

        /// <summary>Runs the provider to completion on this thread.
        ///
        /// The Navisworks API is main-thread only, so there is no worker to
        /// move this to. Instead the provider reports often, and Report pumps
        /// the dispatcher so the bar paints and Cancel is clickable. Everything
        /// else is disabled for the duration, and Refresh is inert while Busy,
        /// so a pumped input event cannot re-enter this.</summary>
        private static void RunPreview(Window window)
        {
            var parts = PartsOf(window);
            if (parts.Busy) return;
            var config = ConfigFrom(parts);

            parts.Busy = true;
            parts.CancelRequested = false;
            // Clicking Preview commits to the selection on screen. The counting
            // pass paused below resumes after this run and would otherwise
            // auto-tick a test with loose clashes, growing TestIndexes and
            // staling the preview the user just waited minutes for, with no
            // user action at all. Nothing may re-tick behind a preview.
            parts.SelectionTouched = true;
            parts.StaleNote = "";
            parts.BarFraction = 0;
            // Seed from the clock, never 0. Environment.TickCount is a signed
            // 32-bit value that spends half of every ~49.7 day cycle negative,
            // and "now - 0" is then negative forever, so a 0 sentinel would
            // wedge the >= 40 gate shut and kill every pump. Two real samples
            // subtract correctly across the rollover; a sentinel does not.
            // The -1000 makes the first report pump immediately.
            parts.LastPump = Environment.TickCount - 1000;
            parts.PreviewTotal = SelectedClashCount(parts);
            parts.State = PreviewState.Working;
            PaintPreview(parts);
            SetInteractive(parts, false);
            try
            {
                var data = parts.Preview(config,
                    (fraction, stage) => Report(window, parts, fraction, stage));
                if (data == null)
                {
                    // Null is the cancel answer, not a failure.
                    if (parts.Data != null && parts.Signature == SignatureOf(config))
                    {
                        // A cancelled RE-preview of settings that have not
                        // changed: the result already on screen is still the one
                        // these settings produce, so it stays and Apply stays
                        // live. Without a note the panel would look completely
                        // untouched and Cancel would read as broken, so the note
                        // is written for Done - it reports the run that just
                        // ended, and must not suggest re-previewing.
                        parts.State = PreviewState.Done;
                        parts.StaleNote = "Preview cancelled; still showing your last preview.";
                        parts.NoteFor = PreviewState.Done;
                    }
                    else
                    {
                        parts.State = PreviewState.Ready;
                        parts.StaleNote = "Preview cancelled.";
                        parts.NoteFor = PreviewState.Ready;
                    }
                }
                else
                {
                    parts.Data = data;
                    parts.Signature = SignatureOf(config);
                    parts.State = PreviewState.Done;
                }
            }
            catch (Exception ex)
            {
                parts.Data = null;
                parts.State = PreviewState.Failed;
                parts.StaleNote = ex.Message;
                parts.NoteFor = PreviewState.Failed;
            }
            finally
            {
                parts.Busy = false;
                SetInteractive(parts, true);
            }
            Refresh(window);
            if (parts.CountPaused)
            {
                parts.CountPaused = false;
                BeginCounting(window);
            }
        }

        /// <summary>Fills the test list's counts one test per dispatcher tick,
        /// after the window is already on screen.
        ///
        /// Counting means walking every result in a test, which on a large
        /// model is the difference between a dialog that opens now and one
        /// that opens in a minute. The rows show … until their number lands.
        /// </summary>
        private static void BeginCounting(Window window)
        {
            var parts = PartsOf(window);
            if (parts.Counts == null || parts.CountIndex >= parts.Tests.Count) return;
            if (parts.Busy)
            {
                // Never fight a running preview for the main thread; RunPreview
                // restarts us when it is finished.
                parts.CountPaused = true;
                return;
            }
            window.Dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)(() =>
            {
                // BeginCounting above only guards *posting* while Busy. This
                // item can still have been posted before a preview started and
                // then get drained by Report's own dispatcher pump while the
                // provider is on the stack (Dispatcher.Invoke(Background)
                // processes everything queued at Background priority ahead of
                // it, this item included). Guard *running* too, or a stray
                // CountOne mid-preview can tick a row and silently turn a
                // preview the user just waited through into Stale the instant
                // it finishes, with no user action. Re-pause instead; RunPreview
                // restarts us when it is finished.
                if (parts.Busy)
                {
                    parts.CountPaused = true;
                    return;
                }
                CountOne(window, parts);
                BeginCounting(window);
            }));
        }

        /// <summary>Total sentinel for a count that threw. Distinct from -1
        /// ("not counted yet") and every non-negative real count, so nothing
        /// that reads Total can mistake a failed count for a genuine zero.</summary>
        private const int CountFailedSentinel = -2;

        /// <summary>Most tests a document may have before the loose-clash
        /// auto-tick stops being a convenience.
        ///
        /// Field report from a real model: 54 clash tests, of which the old
        /// unconditional auto-tick selected 41 totalling 606,361 clashes. That
        /// is a preview nobody would run and 41 boxes to untick by hand. On a
        /// handful of tests the pre-selection genuinely saves work, so it
        /// survives only there; past that the user chooses, helped by the
        /// filter and the All/None buttons.</summary>
        private const int AutoTickMaxTests = 5;

        private static void CountOne(Window window, Parts parts)
        {
            if (parts.CountIndex >= parts.Tests.Count) return;
            var index = parts.CountIndex++;
            var row = parts.Tests[index];
            try
            {
                var counts = parts.Counts(index);
                row.Total = counts != null ? counts.Total : 0;
                row.Grouped = counts != null ? counts.Grouped : 0;
                row.CountError = "";
            }
            catch (Exception ex)
            {
                // A failed count must not render like a genuine zero: the row
                // stays selectable (previewing it surfaces the real failure)
                // but is never auto-ticked, and the cell marks itself as
                // failed rather than counted.
                row.Total = CountFailedSentinel;
                row.Grouped = 0;
                row.CountError = ex.Message;
            }
            if (!parts.SelectionTouched
                && parts.Tests.Count <= AutoTickMaxTests
                && row.Total > row.Grouped)
                parts.TestRows[index].Checked = true;
            PaintTestCounts(parts, index);
            Refresh(window);
        }

        private static void PaintTestCounts(Parts parts, int index)
        {
            var row = parts.Tests[index];
            var ui = parts.TestRows[index];
            if (row.Total == CountFailedSentinel)
            {
                // Same register as the "…" placeholder: one character, no new
                // colour, no new glyph font. The real reason rides the tooltip.
                ui.CountText.Text = "!";
                ui.CountText.ToolTip = row.CountError;
            }
            else
            {
                ui.CountText.Text = row.Total < 0
                    ? "…"
                    : row.Total.ToString("N0", CultureInfo.InvariantCulture);
                ui.CountText.ToolTip = null;
            }
            if (row.Grouped > 0)
            {
                ui.GroupedText.Text = string.Format(CultureInfo.InvariantCulture,
                    "{0:N0} grouped", row.Grouped);
                ui.GroupedText.Visibility = Visibility.Visible;
            }
            else
            {
                ui.GroupedText.Visibility = Visibility.Collapsed;
            }
        }

        public static void FillCountsForTest(Window window)
        {
            var parts = PartsOf(window);
            while (parts.CountIndex < parts.Tests.Count) CountOne(window, parts);
        }

        /// <summary>Queues the first counting tick the way
        /// <c>window.ContentRendered</c> does, without running it. Test seam
        /// for the mid-preview interleaving: only this accessor lets a test
        /// leave a real dispatcher-queued continuation pending, the way a
        /// counting pass in progress when the user clicks Preview would.</summary>
        public static void BeginCountingForTest(Window window) => BeginCounting(window);

        /// <summary>One frame at 25fps: often enough to look live, rare enough
        /// that pumping is not the cost.</summary>
        private const int PumpIntervalMs = 40;

        /// <summary>Has enough elapsed since the last pump?
        ///
        /// Both arguments must be real Environment.TickCount samples. That value
        /// is signed 32-bit and spends half of every ~49.7 day cycle negative,
        /// so it wraps MaxValue -> MinValue; unchecked int subtraction of two
        /// genuine samples gives the right elapsed time straight across that
        /// boundary. What it cannot survive is a sentinel: pass 0 for lastPump
        /// while now is negative and the difference is hugely negative, this
        /// returns false forever, and the dialog stops pumping altogether - no
        /// repaint, no input dispatch, no way to click Cancel. Never seed
        /// LastPump with anything but a sample.</summary>
        private static bool PumpDue(int now, int lastPump) => now - lastPump >= PumpIntervalMs;

        private static bool Report(Window window, Parts parts, double fraction, string stage)
        {
            if (parts.CancelRequested) return false;

            var clamped = fraction < 0 ? 0 : (fraction > 1 ? 1 : fraction);
            parts.StageText.Text = stage;
            parts.BarFraction = clamped;
            LayoutBarFill(parts);
            parts.ProgressCount.Text = parts.PreviewTotal > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0:N0} of {1:N0}",
                    (long)(clamped * parts.PreviewTotal), parts.PreviewTotal)
                : "";

            // Flush render and input. 40ms is about one frame at 25fps: often
            // enough to look live, rare enough that pumping is not the cost.
            var now = Environment.TickCount;
            if (PumpDue(now, parts.LastPump) || clamped >= 1.0)
            {
                parts.LastPump = now;
                window.Dispatcher.Invoke((Action)(() => { }), DispatcherPriority.Background);
            }
            return !parts.CancelRequested;
        }

        /// <summary>Locks the dialog down while a preview runs. Disabled, not
        /// dimmed: a dimmed section stays clickable and loses contrast.
        ///
        /// The footer Cancel is in here too. Report pumps the dispatcher, so a
        /// click really can land while the provider is still on the stack, and
        /// closing the window from under a running preview is not survivable
        /// state. The progress panel's own Cancel stays enabled: it is the one
        /// live control for the duration, which is the whole point.</summary>
        private static void SetInteractive(Parts parts, bool on)
        {
            parts.TestsSection.IsEnabled = on;
            parts.ModeSection.IsEnabled = on;
            parts.CancelDialog.IsEnabled = on;
            parts.Apply.IsEnabled = on && parts.Apply.IsEnabled;
            parts.Ungroup.IsEnabled = on && parts.Ungroup.IsEnabled;
        }

        // ---- state machine -----------------------------------------------------

        private static void RefreshPreview(Parts parts, GrouperConfig config)
        {
            if (parts.Busy) return;   // Working owns the panel until it is done

            if (parts.Tests.Count == 0) parts.State = PreviewState.NoTests;
            else if (config.TestIndexes.Count == 0) parts.State = PreviewState.NeedsSelection;
            else if (!config.Smart && config.RuleIds.Count == 0) parts.State = PreviewState.NeedsRule;
            else if (parts.State == PreviewState.Failed) { /* keep showing why */ }
            else if (parts.Data == null)
            {
                // Ready is the funnel every silent state drains into (untick all
                // tests from Failed, then re-tick, and NeedsSelection has already
                // put the Failed check above out of reach). Drop a note that
                // belonged to some other state; keep the one RunPreview just
                // wrote for this state, which is how "Preview cancelled." shows.
                parts.State = PreviewState.Ready;
                if (parts.NoteFor != PreviewState.Ready) parts.StaleNote = "";
            }
            else if (SignatureOf(config) != parts.Signature)
            {
                parts.State = PreviewState.Stale;
                parts.StaleNote = "Settings changed since the last preview.";
                parts.NoteFor = PreviewState.Stale;
            }
            else
            {
                // The note explained why the last result was not on screen. It
                // is on screen now, so the note is no longer true: leaving it
                // set would print "Settings changed..." (or an old exception)
                // over a current result on the paths that call ShowAction. A
                // note written FOR Done is the exception: it reports what the
                // last run did (a cancel) rather than claiming the result is out
                // of date, so it survives until the next run clears it.
                parts.State = PreviewState.Done;
                if (parts.NoteFor != PreviewState.Done) parts.StaleNote = "";
            }

            PaintPreview(parts);
        }

        private static void PaintPreview(Parts parts)
        {
            parts.Readout.Visibility = Visibility.Collapsed;
            parts.PreviewShell.Visibility = Visibility.Collapsed;
            parts.Message.Visibility = Visibility.Collapsed;
            parts.ActionPanel.Visibility = Visibility.Collapsed;
            parts.ProgressPanel.Visibility = Visibility.Collapsed;
            parts.StaleText.Visibility = Visibility.Collapsed;
            parts.Apply.IsEnabled = false;
            parts.Ungroup.IsEnabled = parts.State != PreviewState.NoTests
                && parts.State != PreviewState.NeedsSelection
                && parts.State != PreviewState.Working;

            switch (parts.State)
            {
                case PreviewState.NoTests:
                    ShowMessage(parts, "No clash tests found. Run Clash Detective first.");
                    break;
                case PreviewState.NeedsSelection:
                    ShowMessage(parts, "Choose a clash test above to see a preview.");
                    break;
                case PreviewState.NeedsRule:
                    ShowMessage(parts, "Add at least one rule to group by.");
                    break;
                case PreviewState.Ready:
                    ShowAction(parts, "Preview grouping");
                    break;
                case PreviewState.Working:
                    parts.ProgressPanel.Visibility = Visibility.Visible;
                    parts.Ungroup.IsEnabled = false;
                    break;
                case PreviewState.Failed:
                    ShowMessage(parts, "Preview did not load.");
                    // The reason rides StaleText, not NoteText: NoteText is a
                    // child of the Readout this method just collapsed, so it
                    // would never reach the screen in this state.
                    ShowAction(parts, "Try again");
                    break;
                case PreviewState.Stale:
                    PaintResult(parts);
                    ShowAction(parts, "Preview again");
                    break;
                case PreviewState.Done:
                    if (parts.Data.Groups.Count == 0)
                    {
                        ShowMessage(parts, "No groups to make. Try Smart Group or add a rule.");
                        ShowAction(parts, "Preview again");
                        break;
                    }
                    PaintResult(parts);
                    // A note written for Done (the cancelled re-preview) rides
                    // the readout's own note line: StaleText lives in the action
                    // panel, which this state collapses, so it is the only line
                    // the user can actually read here.
                    if (parts.StaleNote.Length > 0 && parts.NoteFor == PreviewState.Done)
                        parts.NoteText.Text = (parts.NoteText.Text.Length > 0
                            ? parts.NoteText.Text + " " : "") + parts.StaleNote;
                    parts.Apply.IsEnabled = true;
                    break;
            }
        }

        private static void ShowMessage(Parts parts, string text)
        {
            parts.Message.Visibility = Visibility.Visible;
            parts.Message.Text = text;
        }

        private static void ShowAction(Parts parts, string label)
        {
            parts.ActionPanel.Visibility = Visibility.Visible;
            ((TextBlock)parts.PreviewButton.Content).Text = label;
            var clashes = SelectedClashCount(parts);
            var tests = 0;
            foreach (var row in parts.TestRows) if (row.Checked) tests++;
            parts.SelectionSummary.Text = clashes > 0
                ? string.Format(CultureInfo.InvariantCulture,
                    "{0:N0} clashes selected in {1} test{2}.",
                    clashes, tests, tests == 1 ? "" : "s")
                : string.Format(CultureInfo.InvariantCulture,
                    "{0} test{1} selected.", tests, tests == 1 ? "" : "s");
            // NoteFor is the backstop for the clears above: a note only ever
            // explains the state that wrote it, so no arm of the chain can paint
            // one that has outlived its condition, including arms added later.
            if (parts.StaleNote.Length > 0 && parts.NoteFor == parts.State)
            {
                parts.StaleText.Text = parts.StaleNote;
                parts.StaleText.Visibility = Visibility.Visible;
            }
        }

        private static void PaintResult(Parts parts)
        {
            var preview = parts.Data;
            parts.Readout.Visibility = Visibility.Visible;
            parts.PreviewShell.Visibility = Visibility.Visible;
            parts.StatFrom.Text = preview.TotalClashes.ToString("N0", CultureInfo.InvariantCulture);
            parts.StatTo.Text = preview.TotalGroups.ToString("N0", CultureInfo.InvariantCulture);
            parts.NoteText.Text = preview.Note ?? "";

            parts.PreviewList.Children.Clear();
            const int maxRows = 60;
            var shown = Math.Min(preview.Groups.Count, maxRows);
            for (var i = 0; i < shown; i++)
            {
                var group = preview.Groups[i];
                var line = new DockPanel { Margin = new Thickness(12, 0, 12, 0) };
                var count = TabularNumber(parts,
                    group.Count.ToString("N0", CultureInfo.InvariantCulture), 13, parts.T.Muted);
                count.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(count, Dock.Right);
                line.Children.Add(count);
                var name = Text(parts, group.Name, 13, parts.T.Ink);
                name.VerticalAlignment = VerticalAlignment.Center;
                name.TextWrapping = TextWrapping.NoWrap;
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                name.Margin = new Thickness(0, 0, 12, 0);
                line.Children.Add(name);

                var isLast = i == shown - 1 && preview.Groups.Count <= maxRows;
                parts.PreviewList.Children.Add(new Border
                {
                    Height = 32,
                    BorderBrush = Brush(parts.T.Line),
                    BorderThickness = new Thickness(0, 0, 0, isLast ? 0 : 1),
                    Child = line,
                });
            }
            if (preview.Groups.Count > maxRows)
            {
                var trailer = Text(parts, string.Format(CultureInfo.InvariantCulture,
                    "And {0:N0} more groups not shown here.", preview.Groups.Count - maxRows),
                    12, parts.T.Muted);
                trailer.Margin = new Thickness(12, 8, 0, 8);   // list rows are never centered
                parts.PreviewList.Children.Add(trailer);
            }
        }

        // ---- test accessors ----------------------------------------------------

        public static void RunPreviewForTest(Window window) => RunPreview(window);

        public static void RequestCancelForTest(Window window) =>
            PartsOf(window).CancelRequested = true;

        public static string PreviewStateOf(Window window) =>
            PartsOf(window).State.ToString();

        public static string SelectionSummaryOf(Window window) =>
            PartsOf(window).SelectionSummary.Text;

        public static string StaleNoteOf(Window window) => PartsOf(window).StaleNote;

        /// <summary>The readout's own note line as the user can read it: empty
        /// unless the readout carrying it is on screen right now.</summary>
        public static string ReadoutNoteOf(Window window)
        {
            var parts = PartsOf(window);
            return parts.Readout.Visibility == Visibility.Visible ? parts.NoteText.Text : "";
        }

        /// <summary>The stale/failure detail line as the user can actually read
        /// it: empty unless the element carrying it is visible right now.</summary>
        public static string VisibleStaleTextOf(Window window)
        {
            var parts = PartsOf(window);
            return parts.StaleText.Visibility == Visibility.Visible
                && parts.ActionPanel.Visibility == Visibility.Visible
                ? parts.StaleText.Text
                : "";
        }

        public static bool DialogCancelEnabledOf(Window window) =>
            PartsOf(window).CancelDialog.IsEnabled;

        public static bool PreviewCancelEnabledOf(Window window) =>
            PartsOf(window).CancelPreview.IsEnabled;

        public static bool PumpDueForTest(int now, int lastPump) => PumpDue(now, lastPump);
    }
}
