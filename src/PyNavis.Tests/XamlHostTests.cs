using System;
using System.IO;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PyNavis.Runtime.Forms;
using Xunit;

namespace PyNavis.Tests
{
    public class XamlHostTests
    {
        private const string Sample = @"<Window xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Title='T' Width='200' Height='120'>
  <StackPanel><TextBlock x:Name='Body' Text='hello'/><Button x:Name='Ok' Content='OK'/></StackPanel>
</Window>";

        [Fact]
        public void Loads_Window_And_Finds_Named_Elements()
        {
            RunSta(() =>
            {
                var path = Path.Combine(Path.GetTempPath(), "pynavis_xaml_test.xaml");
                File.WriteAllText(path, Sample);
                var window = XamlHost.Load(path);
                Assert.Equal("T", window.Title);
                Assert.NotNull(window.FindName("Ok"));
                Assert.Equal("hello", ((System.Windows.Controls.TextBlock)window.FindName("Body")).Text);
            });
        }

        [Fact]
        public void NonWindow_Root_Gets_Wrapped()
        {
            RunSta(() =>
            {
                var path = Path.Combine(Path.GetTempPath(), "pynavis_xaml_frag.xaml");
                File.WriteAllText(path,
                    "<StackPanel xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'/>");
                var window = XamlHost.Load(path);
                Assert.NotNull(window);
                Assert.IsType<System.Windows.Controls.StackPanel>(window.Content);
            });
        }

        [Fact]
        public void Relative_Image_Source_Resolves_Beside_The_Xaml_File()
        {
            // A bundle ships icon.png next to its .xaml and writes Source="icon.png".
            // Without a base URI that resolves against the Navisworks entry assembly
            // and the load throws.
            RunSta(() =>
            {
                var folder = NewBundleFolder();
                try
                {
                    WritePng(Path.Combine(folder, "icon.png"), 7);
                    var path = Path.Combine(folder, "window.xaml");
                    File.WriteAllText(path,
                        "<Window xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                        "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Title='T'>" +
                        "<Image x:Name='Icon' Source='icon.png'/></Window>");

                    var window = XamlHost.Load(path);

                    var image = (Image)window.FindName("Icon");
                    Assert.NotNull(image.Source);
                    Assert.Equal(7, ((BitmapSource)image.Source).PixelWidth);
                }
                finally { TryDelete(folder); }
            });
        }

        [Fact]
        public void Relative_Image_Source_Resolves_In_A_Wrapped_Fragment_Too()
        {
            RunSta(() =>
            {
                var folder = NewBundleFolder();
                try
                {
                    Directory.CreateDirectory(Path.Combine(folder, "art"));
                    WritePng(Path.Combine(folder, "art", "logo.png"), 5);
                    var path = Path.Combine(folder, "fragment.xaml");
                    File.WriteAllText(path,
                        "<StackPanel xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
                        "<Image Source='art/logo.png'/></StackPanel>");

                    var window = XamlHost.Load(path);

                    var image = (Image)((StackPanel)window.Content).Children[0];
                    Assert.NotNull(image.Source);
                    Assert.Equal(5, ((BitmapSource)image.Source).PixelWidth);
                }
                finally { TryDelete(folder); }
            });
        }

        internal static string NewBundleFolder()
        {
            var folder = Path.Combine(Path.GetTempPath(), "pynavis_xaml_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            return folder;
        }

        internal static void WritePng(string path, int size)
        {
            var pixels = new byte[size * size * 4];
            var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path)) encoder.Save(stream);
        }

        /// <summary>A decoded image may still hold its file open; a leftover temp folder is harmless.</summary>
        internal static void TryDelete(string folder)
        {
            try { Directory.Delete(folder, true); } catch { }
        }

        private static void RunSta(Action test)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try { test(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException("STA action failed: " + failure);
        }
    }
}
