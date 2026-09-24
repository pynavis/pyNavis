using System;
using System.Threading;
using PyNavis.Runtime.Output.Html;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// WebView2 can be installed and still fail to start (blocked user-data folder,
    /// roaming profile, locked-down machine). The window used to log that and stay
    /// "not ready" forever, so every line the script printed sat in a queue behind a
    /// blank window. It now degrades to plain text and shows everything it was given.
    /// </summary>
    public class HtmlOutputFallbackTests
    {
        [Fact]
        public void WhenTheBrowserCannotStart_QueuedOutputIsShownAsPlainText()
        {
            OnSta(() =>
            {
                var window = new HtmlOutputWindow("T");   // never shown: the browser never initializes
                window.AppendText("hello\n", false);
                window.AppendText("Traceback: boom\n", true);

                window.FallBackToText(new InvalidOperationException("user data folder is blocked"));

                Assert.Contains("hello", window.FallbackText);
                Assert.Contains("Traceback: boom", window.FallbackText);
            });
        }

        [Fact]
        public void AfterFallingBack_LaterOutputStillArrives()
        {
            OnSta(() =>
            {
                var window = new HtmlOutputWindow("T");
                window.FallBackToText(new InvalidOperationException("x"));

                window.AppendText("after\n", false);
                window.AppendHtml("<b>rich</b>");

                Assert.Contains("after", window.FallbackText);
                Assert.Contains("<b>rich</b>", window.FallbackText);   // source shown, like the classic text window
            });
        }

        [Fact]
        public void BeforeAnyFailure_ThereIsNoFallbackText()
        {
            OnSta(() =>
            {
                var window = new HtmlOutputWindow("T");
                window.AppendText("queued\n", false);

                Assert.Null(window.FallbackText);
            });
        }

        // A plain <a href="https://..."> a script prints used to do nothing (or open a
        // bare browser popup inside Navisworks). Web and mail links go to the user's
        // default handler; everything else (the page itself, about:blank) stays put.
        [Theory]
        [InlineData("https://example.com/docs", true)]
        [InlineData("http://example.com", true)]
        [InlineData("mailto:someone@example.com", true)]
        [InlineData("about:blank", false)]
        [InlineData("data:text/html,hi", false)]
        [InlineData("file:///C:/Windows/System32/calc.exe", false)]
        [InlineData("javascript:alert(1)", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void OnlyWebAndMailLinks_OpenOutsideTheWindow(string uri, bool expected)
        {
            Assert.Equal(expected, HtmlOutputWindow.IsExternalLink(uri));
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
