using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PyNavis.Runtime.Ai
{
    /// <summary>Writes the four icon variants of a bundle. An interface so the writer
    /// tests need no WPF.</summary>
    public interface IIconWriter
    {
        void Write(string bundleDir, IconSpec spec);
    }

    /// <summary>
    /// The in-app stand-in for tools/make_icons.py, which needs Pillow and never ships:
    /// a rounded square in one of five accents with one or two letters on it. Every
    /// generated tool looks like this, and none of the accents is the shipped blue, so
    /// a tool the assistant wrote is always recognisable on the ribbon.
    /// </summary>
    public sealed class IconGenerator : IIconWriter
    {
        private const int LargePx = 96;
        private const int SmallPx = 32;

        private static readonly Dictionary<string, (Color light, Color dark)> Palette =
            new Dictionary<string, (Color, Color)>(StringComparer.OrdinalIgnoreCase)
            {
                ["violet"] = (Rgb(0x7A, 0x3F, 0xD6), Rgb(0xB4, 0x8C, 0xFF)),
                ["teal"]   = (Rgb(0x0F, 0x8B, 0x8D), Rgb(0x4F, 0xD1, 0xC5)),
                ["amber"]  = (Rgb(0xC2, 0x6A, 0x00), Rgb(0xFF, 0xB8, 0x4D)),
                ["rose"]   = (Rgb(0xC4, 0x2B, 0x6B), Rgb(0xFF, 0x7E, 0xB6)),
                ["green"]  = (Rgb(0x2E, 0x8B, 0x3A), Rgb(0x6F, 0xD9, 0x7A)),
            };

        private static Color Rgb(int r, int g, int b) => Color.FromRgb((byte)r, (byte)g, (byte)b);

        public static (Color light, Color dark) Accent(string colour) =>
            Palette.TryGetValue(colour ?? "", out var pair) ? pair : Palette[IconSpec.DefaultColour];

        public void Write(string bundleDir, IconSpec spec)
        {
            Directory.CreateDirectory(bundleDir);
            Save(Path.Combine(bundleDir, "icon.png"), Render(spec, false, LargePx));
            Save(Path.Combine(bundleDir, "icon.dark.png"), Render(spec, true, LargePx));
            Save(Path.Combine(bundleDir, "icon.small.png"), Render(spec, false, SmallPx));
            Save(Path.Combine(bundleDir, "icon.small.dark.png"), Render(spec, true, SmallPx));
        }

        public static BitmapSource Render(IconSpec spec, bool dark, int px)
        {
            var (light, darkAccent) = Accent(spec.Colour);
            var accent = dark ? darkAccent : light;
            // Letters read against the accent: near-black on the bright dark-theme
            // accent, white on the deeper light-theme one.
            var ink = dark ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Colors.White;

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var inset = px * 0.06;
                var radius = px * 0.18;
                dc.DrawRoundedRectangle(new SolidColorBrush(accent), null,
                    new Rect(inset, inset, px - 2 * inset, px - 2 * inset), radius, radius);

                var size = spec.Letters.Length > 1 ? px * 0.46 : px * 0.58;
                var text = new FormattedText(spec.Letters, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                    size, new SolidColorBrush(ink), 1.0);
                var origin = new Point((px - text.Width) / 2, (px - text.Height) / 2);
                dc.DrawText(text, origin);
            }

            var bitmap = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        private static void Save(string path, BitmapSource image)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(path)) encoder.Save(stream);
        }
    }
}
