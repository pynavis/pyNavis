using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using PyNavis.Runtime.Bundles;
using PyNavis.Runtime.Panes;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>pane.xaml loading: what a dockpane bundle's XAML may reference.</summary>
    public class PaneContentBuilderTests
    {
        [Fact]
        public void Relative_Image_Source_In_Pane_Xaml_Resolves_Beside_The_File()
        {
            OnSta(() =>
            {
                var folder = XamlHostTests.NewBundleFolder();
                try
                {
                    XamlHostTests.WritePng(Path.Combine(folder, "icon.png"), 9);
                    var path = Path.Combine(folder, "pane.xaml");
                    File.WriteAllText(path,
                        "<StackPanel xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                        "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>" +
                        "<Image x:Name='Icon' Source='icon.png'/></StackPanel>");
                    var model = new DockPaneModel
                    {
                        Title = "Test pane",
                        Directory = folder,
                        XamlPath = path,
                        BundleKey = "t/Test.dockpane",
                    };

                    using (var shell = new PaneShell(1))
                    {
                        var content = PaneContentBuilder.Build(model, shell);

                        // On a load failure Build returns the shell's error message instead.
                        var image = ((FrameworkElement)content).FindName("Icon") as Image;
                        Assert.NotNull(image);
                        Assert.NotNull(image.Source);
                        Assert.Equal(9, ((BitmapSource)image.Source).PixelWidth);
                    }
                }
                finally { XamlHostTests.TryDelete(folder); }
            });
        }

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
    }
}
