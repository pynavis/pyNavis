using System;
using System.Threading;
using PyNavis.Runtime.Forms;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// Fluent dialog layer: pure accent-color math plus STA construction smoke tests
    /// (windows are built but never shown - catches code-only-WPF wiring errors).
    /// </summary>
    public class FluentFormsTests
    {
        [Theory]
        [InlineData(0xFFC06700u, 0x00, 0x67, 0xC0)] // ABGR dword -> RGB (WinUI blue)
        [InlineData(0xFF0000FFu, 0xFF, 0x00, 0x00)] // pure red stored as ABGR
        public void WindowsAccent_AbgrDword_ParsesToRgb(uint abgr, int r, int g, int b)
        {
            var (pr, pg, pb) = FluentChrome.ParseAbgr(abgr);
            Assert.Equal((r, g, b), (pr, pg, pb));
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

        [Fact]
        public void AlertWindow_Builds_WithMessageAndTitle()
        {
            OnSta(() =>
            {
                var window = Dialogs.BuildAlert("hello world", "My Title");
                Assert.Equal("My Title", window.Title);
                Assert.Contains("hello world", Dialogs.MessageTextOf(window));
            });
        }

        [Fact]
        public void ConfirmWindow_Builds_WithYesAndNoButtons()
        {
            OnSta(() =>
            {
                var window = Dialogs.BuildConfirm("sure?", "pyNavis");
                Assert.Equal(2, Dialogs.ButtonCountOf(window));
            });
        }

        [Fact]
        public void AskStringWindow_Builds_WithDefaultText()
        {
            OnSta(() =>
            {
                var window = Dialogs.BuildAskString("Name?", "default-value", "pyNavis");
                Assert.Equal("default-value", Dialogs.InputTextOf(window));
            });
        }

        [Fact]
        public void OutputWindow_Constructs_WithFluentHeader()
        {
            OnSta(() =>
            {
                var window = new Runtime.Output.OutputWindow("t");
                Assert.NotNull(window); // construction exercises header + theming wiring
            });
        }

        [Fact]
        public void FluentChrome_Apply_BeforeShow_DoesNotThrow()
        {
            OnSta(() =>
            {
                var window = Dialogs.BuildAlert("x", "y");
                FluentChrome.Apply(window); // no HWND yet - must defer, not crash
            });
        }
    }
}
