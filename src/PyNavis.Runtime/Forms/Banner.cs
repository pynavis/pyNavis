using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PyNavis.Runtime.Forms
{
    /// <summary>
    /// The pyNavis result banner: a bar the full width of the Navisworks window,
    /// sitting on its bottom edge, filled solid in the level colour with the message
    /// in large type on top of it. Where a toast is a note in the corner, a banner
    /// is a readout: the one number a measuring tool produces, legible from across
    /// the room. There is only ever one; a new banner replaces the last. It never
    /// takes focus, dismisses itself after a while, and a click dismisses it at once.
    ///
    /// Geometry and construction are separated from showing, so both are testable
    /// without a live window.
    /// </summary>
    public static class Banner
    {
        // ---- geometry (pure) ---------------------------------------------------

        /// <summary>The bar's rectangle: the host's full width, flush with its bottom edge.</summary>
        public static Rect RectFor(Rect host, double height) =>
            new Rect(host.Left, host.Bottom - height, host.Width, height);

        // ---- construction (test seam) -----------------------------------------

        private sealed class Parts
        {
            public Border Bar;
            public TextBlock Message;
            public TextBlock Detail;
            public DispatcherTimer Timer;
        }

        private static Window _live;

        private static Parts PartsOf(Window window) => (Parts)window.Tag;

        public static Border BarOf(Window window) => PartsOf(window).Bar;
        public static TextBlock MessageBlockOf(Window window) => PartsOf(window).Message;
        public static TextBlock DetailBlockOf(Window window) => PartsOf(window).Detail;
        public static string MessageTextOf(Window window) => PartsOf(window).Message.Text;
        public static string DetailTextOf(Window window) => PartsOf(window).Detail.Text;

        /// <summary>A banner is meant to be read, not glanced at, so it outstays a toast.</summary>
        public static TimeSpan DurationFor(ToastLevel level)
        {
            switch (level)
            {
                case ToastLevel.Error: return TimeSpan.FromSeconds(12);
                case ToastLevel.Warning: return TimeSpan.FromSeconds(10);
                default: return TimeSpan.FromSeconds(8);
            }
        }

        /// <summary>Constructs a banner without showing it: the test seam.</summary>
        public static Window Build(ToastLevel level, string message, string detail)
        {
            var t = DesignSystem.Tokens.Current;
            var fill = Toast.ColorFor(t, level);
            var ink = DesignSystem.OnAccent(fill);
            var parts = new Parts();

            parts.Message = DesignSystem.Text(message ?? "", 22, ink, FontWeights.SemiBold);
            parts.Message.TextAlignment = TextAlignment.Center;
            parts.Detail = DesignSystem.Text(detail ?? "", 14, ink);
            parts.Detail.TextAlignment = TextAlignment.Center;
            parts.Detail.Opacity = 0.85;
            parts.Detail.Margin = new Thickness(0, 4, 0, 0);
            parts.Detail.Visibility = string.IsNullOrEmpty(detail)
                ? Visibility.Collapsed
                : Visibility.Visible;

            var lines = new StackPanel
            {
                Margin = new Thickness(24, 14, 24, 14),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            lines.Children.Add(parts.Message);
            lines.Children.Add(parts.Detail);

            // Solid fill edge to edge: the colour is the message's level and the
            // bar has no chrome around it to read the colour against.
            parts.Bar = new Border
            {
                Background = DesignSystem.Brush(fill),
                SnapsToDevicePixels = true,
                Child = lines,
            };

            var window = new Window
            {
                SizeToContent = SizeToContent.Height,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowActivated = false,
                ShowInTaskbar = false,
                Topmost = true,
                ResizeMode = ResizeMode.NoResize,
                Focusable = false,
                Content = parts.Bar,
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
        public static void Show(string level, string message, string detail) =>
            Show(level, message, detail, double.NaN);

        /// <summary>
        /// As above with the stay in seconds: NaN or negative for the level's own duration,
        /// zero to stay until dismissed (a prompt the tool is waiting on).
        /// </summary>
        public static void Show(string level, string message, string detail, double seconds)
        {
            try
            {
                var parsed = Toast.ParseLevel(level);
                var stay = StayFor(parsed, seconds);
                var dispatcher = Application.Current != null
                    ? Application.Current.Dispatcher
                    : Dispatcher.CurrentDispatcher;

                if (dispatcher.CheckAccess()) ShowCore(parsed, message, detail, stay);
                else dispatcher.BeginInvoke(new Action(() => ShowCore(parsed, message, detail, stay)));
            }
            catch (Exception ex)
            {
                // A banner is never worth failing a script over.
                Log.Error($"Banner '{message}' could not be shown", ex);
            }
        }

        /// <summary>The stay a Show request resolves to; Zero means until dismissed.</summary>
        public static TimeSpan StayFor(ToastLevel level, double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0) return DurationFor(level);
            return TimeSpan.FromSeconds(seconds);
        }

        private static void ShowCore(ToastLevel level, string message, string detail, TimeSpan stay)
        {
            CloseAll();                                   // one banner at a time

            var window = Build(level, message, detail);
            var parts = PartsOf(window);

            window.SourceInitialized += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE);
                Reposition(window);
            };
            window.SizeChanged += (s, e) => Reposition(window);

            if (stay > TimeSpan.Zero)
            {
                parts.Timer = new DispatcherTimer { Interval = stay };
                parts.Timer.Tick += (s, e) => Dismiss(window);

                // Hovering holds the banner open long enough to read it.
                window.MouseEnter += (s, e) => parts.Timer.Stop();
                window.MouseLeave += (s, e) => parts.Timer.Start();
            }

            _live = window;
            window.Opacity = 0;
            window.Show();
            window.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
            parts.Timer?.Start();
            Reposition(window);
        }

        private static void Dismiss(Window window)
        {
            if (!ReferenceEquals(_live, window)) return;
            _live = null;
            PartsOf(window).Timer?.Stop();

            var fade = new DoubleAnimation(window.Opacity, 0, TimeSpan.FromMilliseconds(160));
            fade.Completed += (s, e) => { try { window.Close(); } catch { } };
            window.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        /// <summary>Closes the live banner, if any. Called on Reload so it never outlives the runtime.</summary>
        public static void CloseAll()
        {
            var window = _live;
            _live = null;
            if (window == null) return;
            PartsOf(window).Timer?.Stop();
            try { window.Close(); } catch { }
        }

        private static void Reposition(Window window)
        {
            var source = PresentationSource.FromVisual(window);
            if (source == null) return;

            var host = HostRect();
            var toDevice = source.CompositionTarget.TransformToDevice;
            var fromDevice = source.CompositionTarget.TransformFromDevice;
            var heightDevice = toDevice.Transform(new Point(0, window.ActualHeight)).Y;
            var rect = RectFor(host, heightDevice);

            var topLeft = fromDevice.Transform(new Point(rect.X, rect.Y));
            var size = fromDevice.Transform(new Point(rect.Width, rect.Height));
            window.Left = topLeft.X;
            window.Top = topLeft.Y;
            window.Width = size.X;
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
                Log.Error("Could not read the host window rect for banner placement", ex);
            }
            var work = SystemParameters.WorkArea;
            return new Rect(work.Left, work.Top, work.Width, work.Height);
        }
    }
}
