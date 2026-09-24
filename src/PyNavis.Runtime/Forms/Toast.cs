using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PyNavis.Runtime.Forms
{
    /// <summary>What a toast is saying: picks its border and rail colour.</summary>
    public enum ToastLevel
    {
        Success,
        Error,
        Info,
        Warning,
    }

    /// <summary>
    /// The pyNavis status toast: a small neutral card in the bottom-right corner of
    /// the Navisworks window, with a semantic border all around and a heavier rail on
    /// the left edge. It never takes focus and dismisses itself, so tools that run
    /// constantly can report what they did without opening the output window.
    ///
    /// Geometry and construction are separated from showing, so both are testable
    /// without a live window.
    /// </summary>
    public static class Toast
    {
        public const double Width = 300;
        public const double Margin = 16;
        public const double Gap = 8;
        public const int MaxVisible = 3;

        // ---- geometry (pure) ---------------------------------------------------

        /// <summary>
        /// Where the toast at stack position <paramref name="index"/> goes, in the same
        /// units as <paramref name="host"/>. Index 0 is nearest the bottom-right corner
        /// and later toasts stack upward.
        /// </summary>
        /// <param name="below">
        /// Summed HEIGHT of the toasts between this one and the corner, gaps excluded
        /// (<paramref name="index"/> supplies those). Zero for index 0.
        /// </param>
        /// <remarks>
        /// This used to be <c>index * (size.Height + gap)</c>, which quietly assumes every
        /// toast is the same height - it scales one toast's own height by how many are
        /// under it. Toasts are not uniform: one carrying a detail line is roughly twice
        /// the height of one without. Field-measured, a one-line toast stacked
        /// above a two-line one was drawn on top of it, and the mirror case left a hole.
        /// Every geometry test used 64, 64, 64, so the assumption went unexercised.
        /// </remarks>
        public static Rect RectFor(Rect host, int index, Size size, double margin, double gap,
            double below = 0)
        {
            var x = host.Right - margin - size.Width;
            var y = host.Bottom - margin - size.Height - below - index * gap;
            return new Rect(x, y, size.Width, size.Height);
        }

        // ---- construction (test seam) -----------------------------------------

        private sealed class Parts
        {
            public Border Card;
            public Border Rail;
            public TextBlock Message;
            public TextBlock Detail;
            public DispatcherTimer Timer;
        }

        private static readonly List<Window> Live = new List<Window>();

        private static Parts PartsOf(Window window) => (Parts)window.Tag;

        public static Border CardOf(Window window) => PartsOf(window).Card;
        public static Border RailOf(Window window) => PartsOf(window).Rail;
        public static TextBlock DetailBlockOf(Window window) => PartsOf(window).Detail;
        public static string MessageTextOf(Window window) => PartsOf(window).Message.Text;
        public static string DetailTextOf(Window window) => PartsOf(window).Detail.Text;

        public static Color ColorFor(DesignSystem.Tokens t, ToastLevel level)
        {
            switch (level)
            {
                case ToastLevel.Success: return t.Success;
                case ToastLevel.Error: return t.Error;
                case ToastLevel.Warning: return t.Warning;
                default: return t.Info;
            }
        }

        /// <summary>An error carries something to read, so it lingers.</summary>
        public static TimeSpan DurationFor(ToastLevel level)
        {
            switch (level)
            {
                case ToastLevel.Error: return TimeSpan.FromSeconds(7);
                case ToastLevel.Warning: return TimeSpan.FromSeconds(5);
                default: return TimeSpan.FromSeconds(4);
            }
        }

        /// <summary>Constructs a toast without showing it: the test seam.</summary>
        public static Window Build(ToastLevel level, string message, string detail)
        {
            var t = DesignSystem.Tokens.Current;
            var accent = ColorFor(t, level);
            var parts = new Parts();

            parts.Message = DesignSystem.Text(message ?? "", 13, t.Ink, FontWeights.SemiBold);
            parts.Detail = DesignSystem.Text(detail ?? "", 12, t.Muted);
            parts.Detail.Margin = new Thickness(0, 3, 0, 0);
            parts.Detail.Visibility = string.IsNullOrEmpty(detail)
                ? Visibility.Collapsed
                : Visibility.Visible;

            var lines = new StackPanel { Margin = new Thickness(14, 11, 14, 11) };
            lines.Children.Add(parts.Message);
            lines.Children.Add(parts.Detail);

            parts.Rail = new Border { Width = 3, Background = DesignSystem.Brush(accent) };

            var row = new DockPanel();
            DockPanel.SetDock(parts.Rail, Dock.Left);
            row.Children.Add(parts.Rail);
            row.Children.Add(lines);

            // The semantic colour rings the whole card, not just the rail: a
            // toast is a transient overlay with no surrounding chrome to read
            // it against, so the level has to be legible at a glance.
            parts.Card = new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = DesignSystem.Brush(t.Paper),
                BorderBrush = DesignSystem.Brush(accent),
                BorderThickness = new Thickness(1.5),
                SnapsToDevicePixels = true,
                Child = row,
            };

            var window = new Window
            {
                Width = Width,
                SizeToContent = SizeToContent.Height,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowActivated = false,
                ShowInTaskbar = false,
                Topmost = true,
                ResizeMode = ResizeMode.NoResize,
                Focusable = false,
                Content = parts.Card,
                Tag = parts,
            };
            window.MouseLeftButtonDown += (s, e) => Dismiss(window);
            return window;
        }

        // ---- showing -----------------------------------------------------------

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        /// <summary>Script-facing entry: level is "success", "error", "info" or "warning".</summary>
        public static void Show(string level, string message, string detail)
        {
            try
            {
                var parsed = ParseLevel(level);
                var dispatcher = Application.Current != null
                    ? Application.Current.Dispatcher
                    : Dispatcher.CurrentDispatcher;

                if (dispatcher.CheckAccess()) ShowCore(parsed, message, detail);
                else dispatcher.BeginInvoke(new Action(() => ShowCore(parsed, message, detail)));
            }
            catch (Exception ex)
            {
                // A toast is never worth failing a script over.
                Log.Error($"Toast '{message}' could not be shown", ex);
            }
        }

        private static ToastLevel ParseLevel(string level)
        {
            switch ((level ?? "").Trim().ToLowerInvariant())
            {
                case "error": return ToastLevel.Error;
                case "warning": return ToastLevel.Warning;
                case "info": return ToastLevel.Info;
                default: return ToastLevel.Success;
            }
        }

        private static void ShowCore(ToastLevel level, string message, string detail)
        {
            while (Live.Count >= MaxVisible) Dismiss(Live[Live.Count - 1]);

            var window = Build(level, message, detail);
            var parts = PartsOf(window);

            window.SourceInitialized += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE);
                Reposition();
            };

            parts.Timer = new DispatcherTimer { Interval = DurationFor(level) };
            parts.Timer.Tick += (s, e) => Dismiss(window);

            // Hovering holds the toast open long enough to read it.
            window.MouseEnter += (s, e) => parts.Timer.Stop();
            window.MouseLeave += (s, e) => parts.Timer.Start();

            Live.Insert(0, window);
            window.Opacity = 0;
            window.Show();
            window.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
            parts.Timer.Start();
            Reposition();
        }

        private static void Dismiss(Window window)
        {
            if (!Live.Remove(window)) return;
            PartsOf(window).Timer?.Stop();

            var fade = new DoubleAnimation(window.Opacity, 0, TimeSpan.FromMilliseconds(160));
            fade.Completed += (s, e) => { try { window.Close(); } catch { } };
            window.BeginAnimation(UIElement.OpacityProperty, fade);
            Reposition();
        }

        /// <summary>Closes every live toast. Called on Reload so none outlive the runtime.</summary>
        public static void CloseAll()
        {
            foreach (var window in Live.ToArray())
            {
                PartsOf(window).Timer?.Stop();
                try { window.Close(); } catch { }
            }
            Live.Clear();
        }

        private static void Reposition()
        {
            var host = HostRect();
            // Carries the heights already placed, so a taller toast pushes the ones above
            // it further up rather than being sat on. Only toasts that were actually
            // positioned count towards it; one still without a source occupies no space.
            var below = 0.0;
            var placed = 0;
            for (var i = 0; i < Live.Count; i++)
            {
                var window = Live[i];
                var source = PresentationSource.FromVisual(window);
                if (source == null) continue;

                var sizeDevice = source.CompositionTarget.TransformToDevice.Transform(
                    new Point(window.ActualWidth, window.ActualHeight));
                var rect = RectFor(host, placed, new Size(sizeDevice.X, sizeDevice.Y),
                    Margin, Gap, below);
                below += sizeDevice.Y;
                placed++;
                var topLeft = source.CompositionTarget.TransformFromDevice.Transform(
                    new Point(rect.X, rect.Y));
                window.Left = topLeft.X;
                window.Top = topLeft.Y;
            }
        }

        /// <summary>The Navisworks main window in device pixels, or the primary work area.</summary>
        private static Rect HostRect()
        {
            try
            {
                var hwnd = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var r))
                    return new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            }
            catch (Exception ex)
            {
                Log.Error("Could not read the host window rect for toast placement", ex);
            }
            var work = SystemParameters.WorkArea;
            return new Rect(work.Left, work.Top, work.Width, work.Height);
        }
    }
}
