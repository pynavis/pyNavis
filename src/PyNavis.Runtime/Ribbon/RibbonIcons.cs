using System;
using System.Windows.Media.Imaging;

namespace PyNavis.Runtime.Ribbon
{
    /// <summary>
    /// Loads bundle icon PNGs for the ribbon. AdWindows draws images at their
    /// natural WPF size instead of fitting them to the button slot, so every
    /// bitmap is re-stamped with the DPI that makes its logical size match the
    /// slot it fills (32 for large buttons, 16 for small rows and menu lists).
    /// The full pixel data is kept, so 96px art stays crisp at high display
    /// scaling instead of being downsampled.
    /// </summary>
    public static class RibbonIcons
    {
        public static BitmapSource Load(string path, int logicalSize)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.CacheOption = BitmapCacheOption.OnLoad; // no file lock after load
                image.EndInit();

                var dpi = 96.0 * image.PixelWidth / logicalSize;
                var stride = (image.PixelWidth * image.Format.BitsPerPixel + 7) / 8;
                var pixels = new byte[stride * image.PixelHeight];
                image.CopyPixels(pixels, stride, 0);
                var sized = BitmapSource.Create(image.PixelWidth, image.PixelHeight,
                    dpi, dpi, image.Format, image.Palette, pixels, stride);
                sized.Freeze();
                return sized;
            }
            catch (Exception ex)
            {
                Log.Error($"Could not load icon '{path}'", ex);
                return null;
            }
        }
    }
}
