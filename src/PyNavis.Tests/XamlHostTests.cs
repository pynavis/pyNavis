using System;
using System.IO;
using System.Threading;
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
