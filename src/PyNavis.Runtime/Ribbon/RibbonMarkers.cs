using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PyNavis.Runtime.Config;

namespace PyNavis.Runtime.Ribbon
{
    /// <summary>
    /// The two quiet hints a pyNavis button can carry: a dot on the icon when the
    /// bundle has a config.py (the Shift+Click secondary action), and a marker after
    /// the caption when a keyboard chord resolves to it.
    ///
    /// Both are read from config.json once per ribbon build, so Reload picks up an
    /// edit without a restart. The text half is pure and unit tested; the dot half
    /// needs WPF and is exercised through its size and DPI rather than its pixels.
    /// </summary>
    public static class RibbonMarkers
    {
        /// <summary>Draw a dot on buttons that have a config.py. "ribbon.configDot".</summary>
        public static bool ConfigDot { get; private set; } = true;

        /// <summary>Appended to a button caption when a chord resolves to it.
        /// "ribbon.shortcutMarker". Empty turns it off.</summary>
        public static string ShortcutMarker { get; private set; } = "●";

        /// <summary>Re-reads both settings. Called once per ribbon build.</summary>
        public static void Configure(PyNavisConfig config)
        {
            ConfigDot = config == null || config.RibbonConfigDot;
            ShortcutMarker = config == null ? "●" : (config.RibbonShortcutMarker ?? "");
        }

        /// <summary>Test seam: puts both settings back where a fresh install has them.</summary>
        internal static void ResetForTests()
        {
            ConfigDot = true;
            ShortcutMarker = "●";
        }

        /// <summary>
        /// The caption a button with a chord should show.
        ///
        /// The marker joins the LAST line, because a large button's RibbonTitle carries
        /// its own newline to wrap a long name onto two lines and appending past that
        /// would strand the marker beside the first word. A marker that starts with a
        /// newline is taken at its word and gets its own line, which is how a caller
        /// asks for the marker under the name rather than beside it.
        /// </summary>
        public static string WithShortcutMarker(string caption)
        {
            if (string.IsNullOrEmpty(ShortcutMarker)) return caption;
            if (string.IsNullOrEmpty(caption)) return caption;
            if (ShortcutMarker.StartsWith("\n", StringComparison.Ordinal)
                || ShortcutMarker.StartsWith("\r", StringComparison.Ordinal))
                return caption + ShortcutMarker;
            return caption + " " + ShortcutMarker;
        }

        /// <summary>
        /// The same bitmap with a filled accent dot in its bottom right corner.
        ///
        /// The dot is drawn in the bitmap's OWN pixel space and the result is re-stamped
        /// with the source's DPI, because AdWindows draws ribbon images at their natural
        /// WPF size rather than fitting them to the slot: losing the DPI here would put a
        /// 96px glyph back on a 32px button. A paper-coloured ring sits under the dot so
        /// it stays legible on top of dark glyph strokes.
        ///
        /// Returns the source unchanged if anything fails, so a button that cannot be
        /// decorated still gets its icon.
        /// </summary>
        public static BitmapSource WithConfigDot(BitmapSource source)
        {
            if (source == null) return null;
            try
            {
                var w = source.PixelWidth;
                var h = source.PixelHeight;

                // Proportional to the art, so 96px and 32px sources get the same dot
                // relative to the glyph instead of one being a speck and one a blob.
                // 0.24 of the width is the largest that still leaves the glyph
                // readable: measured on the ribbon's real 32 and 16 unit slots, a
                // smaller dot stops registering as a mark and a larger one starts
                // eating the drawing it is supposed to annotate.
                var radius = Math.Max(3.0, w * 0.24);
                var inset = radius + Math.Max(1.0, w * 0.03);
                var centre = new Point(w - inset, h - inset);

                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawImage(source, new Rect(0, 0, w, h));
                    dc.DrawEllipse(new SolidColorBrush(PaperColor()), null,
                        centre, radius * 1.28, radius * 1.28);
                    dc.DrawEllipse(new SolidColorBrush(Forms.DesignSystem.Accent), null,
                        centre, radius, radius);
                }

                var target = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                target.Render(visual);

                // Re-stamp the source's DPI: RenderTargetBitmap always renders at 96,
                // and the whole point of RibbonIcons.Load is that the DPI carries the
                // logical size the slot needs.
                var stride = (w * PixelFormats.Pbgra32.BitsPerPixel + 7) / 8;
                var pixels = new byte[stride * h];
                target.CopyPixels(pixels, stride, 0);
                var stamped = BitmapSource.Create(w, h, source.DpiX, source.DpiY,
                    PixelFormats.Pbgra32, null, pixels, stride);
                stamped.Freeze();
                return stamped;
            }
            catch (Exception ex)
            {
                Log.Error("Could not draw the config dot - keeping the plain icon.", ex);
                return source;
            }
        }

        /// <summary>The surface the dot's ring is drawn in, so it reads on either theme.</summary>
        private static Color PaperColor() =>
            Output.PyNavisTheme.IsDark ? Color.FromRgb(32, 32, 32) : Colors.White;
    }
}
