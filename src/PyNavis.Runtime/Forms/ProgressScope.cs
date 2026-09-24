using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PyNavis.Runtime.Output;

namespace PyNavis.Runtime.Forms
{
    /// <summary>
    /// A progress window for a long synchronous write, with input to Navisworks
    /// switched off for its lifetime.
    ///
    /// Why input has to be off. Scripts run on the UI thread, so showing progress
    /// at all means pumping the message loop (see UiPump), and pumping dispatches
    /// input to the WHOLE application - not just to pyNavis. During a clash-group
    /// commit that is dangerous: the user could re-run a clash test between two of
    /// our per-test commits, and the next commit would replace their fresh results
    /// with our already-built copy through TestsReplaceWithCopy. No managed guard
    /// can see that happen, because it never goes through our code. Silent data
    /// loss beats a visible error every time, so the safe move is to make it
    /// unreachable.
    ///
    /// Why not ShowDialog. Modality is what we want, but ShowDialog blocks, and the
    /// work loop lives in Python (apply_grouping walks the selected tests). Driving
    /// it from a window event would mean marshalling the whole loop into a delegate.
    /// Showing non-modally and calling EnableWindow(host, false) gives the same
    /// input confinement with the script's control flow left alone. Owned windows
    /// are not disabled along with their owner, so this window - and any Cancel
    /// button added to it later - stays live.
    ///
    /// Everything before the commit is side-effect-free (pynavis.clash builds a
    /// detached copy), so a Cancel here is clean: canCancel adds a Secondary
    /// button that only flips IsCancelled for the caller's own loop to notice
    /// (via forms.progress()'s update()/check()) - it never aborts a write on
    /// its own.
    /// </summary>
    public sealed class ProgressScope : IDisposable
    {
        private sealed class Parts
        {
            public TextBlock Label;
            public Border Bar;
            public bool Cancelled;
        }

        /// <summary>Inner width available to the bar: 380 window less 18px margins.</summary>
        public const double TrackWidth = 344;

        private readonly Window _window;
        private readonly Parts _parts;
        private readonly IntPtr _host;
        private int _lastPump;
        private bool _disposed;

        // Leaked scopes would leave Navisworks unclickable, so ScriptExecutor
        // sweeps this in a finally as a backstop to the script's own try/finally.
        private static readonly List<ProgressScope> Live = new List<ProgressScope>();

        private ProgressScope(Window window, Parts parts, IntPtr host)
        {
            _window = window;
            _parts = parts;
            _host = host;
            _lastPump = UiPump.Seed();
        }

        // ---- construction ------------------------------------------------------

        /// <summary>Builds the window without showing it (the unit-test seam).</summary>
        public static Window Build(string title, string label) => Build(title, label, false);

        /// <summary>Builds the window without showing it (the unit-test seam).
        /// With canCancel, a Secondary "Cancel" button appears below the bar;
        /// clicking it sets the Cancelled flag and relabels to "Cancelling...".</summary>
        public static Window Build(string title, string label, bool canCancel)
        {
            var t = DesignSystem.Tokens.Current;
            var parts = new Parts();

            var heading = DesignSystem.Text(title ?? "", 13, t.Ink, FontWeights.SemiBold);
            parts.Label = DesignSystem.Text(label ?? "", 12, t.Muted);
            parts.Label.Margin = new Thickness(0, 3, 0, 12);

            parts.Bar = new Border
            {
                Width = 0,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = DesignSystem.Brush(DesignSystem.Accent),
                HorizontalAlignment = HorizontalAlignment.Left,
            };

            // Neutral track, accent bar: per design system v2 the accent appears
            // only in the primary action, the selection rail and the focus ring -
            // a determinate bar is this window's primary signal.
            var track = new Border
            {
                Width = TrackWidth,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = DesignSystem.Brush(t.Line),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = parts.Bar,
            };

            var lines = new StackPanel { Margin = new Thickness(18) };
            lines.Children.Add(heading);
            lines.Children.Add(parts.Label);
            lines.Children.Add(track);

            if (canCancel)
            {
                var cancel = DesignSystem.Secondary(t, "Cancel", () => CancelClicked(parts));
                cancel.HorizontalAlignment = HorizontalAlignment.Right;
                cancel.Margin = new Thickness(0, 12, 0, 0);
                lines.Children.Add(cancel);
            }

            var card = new Border
            {
                Background = DesignSystem.Brush(t.Paper),
                BorderBrush = DesignSystem.Brush(t.FrameLine),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                SnapsToDevicePixels = true,
                Child = lines,
            };

            // No titlebar and no close button on purpose: closing this window
            // while the write is still running would leave the user staring at a
            // frozen Navisworks with no explanation.
            return new Window
            {
                Width = 380,
                SizeToContent = SizeToContent.Height,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brushes.Transparent,
                AllowsTransparency = true,
                Content = card,
                Tag = parts,
            };
        }

        /// <summary>Shows the window and takes input away from Navisworks.</summary>
        public static ProgressScope Begin(string title, string label) => Begin(title, label, false);

        /// <summary>Shows the window and takes input away from Navisworks.
        /// With canCancel, the window offers a Cancel button; see IsCancelled.</summary>
        public static ProgressScope Begin(string title, string label, bool canCancel)
        {
            var window = Build(title, label, canCancel);
            var parts = (Parts)window.Tag;
            var host = IntPtr.Zero;
            try
            {
                FluentChrome.OwnByHost(window);
                window.Show();
                host = HostHandle();
                if (host != IntPtr.Zero) EnableWindow(host, false);
            }
            catch (Exception ex)
            {
                Log.Error("Could not show the progress window.", ex);
            }
            var scope = new ProgressScope(window, parts, host);
            Live.Add(scope);
            return scope;
        }

        /// <summary>True once the user has clicked Cancel. Scripts poll this via
        /// forms.progress()'s update()/check() between units of work.</summary>
        public bool IsCancelled => _parts.Cancelled;

        // ---- reporting ---------------------------------------------------------

        /// <summary>Bar width for a fraction, clamped. Pure, so it is testable.</summary>
        public static double BarWidthFor(double fraction) =>
            TrackWidth * Math.Max(0.0, Math.Min(1.0, double.IsNaN(fraction) ? 0.0 : fraction));

        /// <summary>Updates the bar and label, pumping at most every
        /// UiPump.IntervalMs. Callers report far more often than that.</summary>
        public void Report(double fraction, string label)
        {
            if (_disposed) return;
            _parts.Bar.Width = BarWidthFor(fraction);
            if (label != null) _parts.Label.Text = label;
            Pump();
        }

        /// <summary>Pumps the message loop on the same throttle Report uses,
        /// without touching the bar or the label.
        ///
        /// A loop that only ever calls forms.progress().check() reports no
        /// progress, so without this it would never pump - and a script owns the
        /// UI thread for its whole run, which means the Cancel button would never
        /// receive its click. IsCancelled could then only ever read false: a dead
        /// button on a live window.</summary>
        public void Pump()
        {
            if (_disposed) return;
            var now = Environment.TickCount;
            if (!UiPump.Due(now, _lastPump)) return;
            _lastPump = now;
            UiPump.Pump(_window.Dispatcher);
        }

        // ---- teardown ----------------------------------------------------------

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Live.Remove(this);
            try
            {
                // Re-enable BEFORE closing: closing the active window while its
                // owner is still disabled lets focus land outside the process.
                if (_host != IntPtr.Zero) EnableWindow(_host, true);
                _window.Close();
            }
            catch (Exception ex)
            {
                Log.Error("Could not close the progress window.", ex);
            }
        }

        /// <summary>
        /// Disposes any scope a script left behind. A leaked scope means a
        /// permanently unclickable Navisworks, so this runs from ScriptExecutor's
        /// finally regardless of how the script ended.
        /// </summary>
        public static void CloseAll()
        {
            foreach (var scope in Live.ToArray()) scope.Dispose();
        }

        // ---- cancel --------------------------------------------------------------

        /// <summary>Shared by the Cancel button's Click handler and CancelForTest,
        /// so the test seam exercises exactly the behavior a real click gets.</summary>
        private static void CancelClicked(Parts parts)
        {
            parts.Cancelled = true;
            parts.Label.Text = "Cancelling...";
        }

        // ---- test accessors ----------------------------------------------------

        public static double BarWidthOf(Window window) => ((Parts)window.Tag).Bar.Width;
        public static string LabelTextOf(Window window) => ((Parts)window.Tag).Label.Text;

        /// <summary>Simulates clicking Cancel, without needing the button on screen.</summary>
        public static void CancelForTest(Window window) => CancelClicked((Parts)window.Tag);

        public static bool CancelledOf(Window window) => ((Parts)window.Tag).Cancelled;

        // ---- interop -----------------------------------------------------------

        private static IntPtr HostHandle()
        {
            try
            {
                return Process.GetCurrentProcess().MainWindowHandle;
            }
            catch (Exception ex)
            {
                Log.Error("Could not find the Navisworks host window.", ex);
                return IntPtr.Zero;
            }
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnableWindow(IntPtr hwnd, bool enable);
    }
}
