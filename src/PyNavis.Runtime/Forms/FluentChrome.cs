using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using PyNavis.Runtime.Output;

namespace PyNavis.Runtime.Forms
{
    /// <summary>
    /// Windows 11 Fluent window chrome for any WPF window (dialogs, console): rounded
    /// corners, titlebar matching the pyNavis theme, and ownership by the Navisworks
    /// host window. Surfaces stay solid: the design system bans glass and acrylic, so
    /// no backdrop is requested. Every DWM call is best-effort - on older Windows the
    /// window simply keeps its themed solid look.
    /// </summary>
    public static class FluentChrome
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// <summary>
        /// Applies Fluent chrome now if the window already has an HWND, otherwise when
        /// one exists. Safe to call any time before or after Show.
        /// </summary>
        public static void Apply(Window window)
        {
            OwnByHost(window);
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero) ApplyToHandle(hwnd);
            else window.SourceInitialized += (s, e) =>
                ApplyToHandle(new WindowInteropHelper(window).Handle);
        }

        /// <summary>
        /// Parents a window to the Navisworks main window. Without an owner a
        /// modal dialog is not modal to the HOST: it sinks behind Navisworks
        /// when the host is clicked, which reads as a hang. A no-op outside
        /// Navisworks (tests, console hosts) where there is no main window.
        /// </summary>
        public static void OwnByHost(Window window)
        {
            try
            {
                if (window.Owner != null) return;
                var host = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (host == IntPtr.Zero) return;
                new WindowInteropHelper(window).Owner = host;
            }
            catch (Exception ex)
            {
                Log.Error("Could not parent the window to the Navisworks host.", ex);
            }
        }

        private static void ApplyToHandle(IntPtr hwnd)
        {
            try
            {
                var dark = PyNavisTheme.IsDark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

                var corners = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corners, sizeof(int));

                // No backdrop. The system bans glass and acrylic on content, and
                // asking for Mica here contradicted that even though our solid
                // Background hid it.
            }
            catch (Exception ex)
            {
                Log.Error("Fluent chrome not applied (older Windows?) - keeping solid style.", ex);
            }
        }

        /// <summary>DWM stores the accent as an ABGR dword; unpack to RGB.</summary>
        public static (int r, int g, int b) ParseAbgr(uint abgr) =>
            ((int)(abgr & 0xFF), (int)((abgr >> 8) & 0xFF), (int)((abgr >> 16) & 0xFF));

        /// <summary>The user's Windows accent color; WinUI blue when unreadable.</summary>
        private static Color? _accent;

        /// <summary>Forgets the cached accent; Reload calls this with the theme.</summary>
        public static void InvalidateAccent() => _accent = null;

        /// <summary>
        /// The Windows accent, read once. DesignSystem.Accent is touched dozens
        /// of times while a dialog builds, and each read was hitting the
        /// registry.
        /// </summary>
        public static Color AccentColor()
        {
            if (_accent.HasValue) return _accent.Value;
            _accent = ReadAccentColor();
            return _accent.Value;
        }

        private static Color ReadAccentColor()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                {
                    if (key?.GetValue("AccentColor") is int raw)
                    {
                        var (r, g, b) = ParseAbgr(unchecked((uint)raw));
                        return Color.FromRgb((byte)r, (byte)g, (byte)b);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Could not read Windows accent color - using default.", ex);
            }
            return Color.FromRgb(0x00, 0x67, 0xC0);
        }
    }
}
