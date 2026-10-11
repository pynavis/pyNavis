using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using PyNavis.Runtime.Output;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The plain-text output window's Copy and Save buttons. Both run on the Navisworks
    /// UI thread, so a locked clipboard or a failed write must be reported, never thrown.
    /// </summary>
    public class OutputWindowTests
    {
        [Fact]
        public void Copy_Puts_All_The_Output_On_The_Clipboard()
        {
            OnSta(() =>
            {
                var window = new OutputWindow("t");
                window.AppendText("first\n", false);
                window.AppendText("boom\n", true);
                string copied = null;
                window.SetClipboardText = text => copied = text;

                window.CopyToClipboard();

                Assert.Equal("first\nboom\n", copied);
            });
        }

        [Fact]
        public void A_Locked_Clipboard_Is_Reported_Not_Thrown()
        {
            OnSta(() =>
            {
                var window = new OutputWindow("t");
                window.AppendText("hello\n", false);
                var failures = Record(window);
                // What Clipboard.SetText throws while another process holds the clipboard.
                window.SetClipboardText = _ =>
                    throw new COMException("OpenClipboard failed", unchecked((int)0x800401D0));

                window.CopyToClipboard();

                var failure = Assert.Single(failures);
                Assert.Equal("Could not copy the output", failure.Item1);
                Assert.Contains("OpenClipboard failed", failure.Item2);
            });
        }

        [Fact]
        public void Save_Writes_All_The_Output()
        {
            OnSta(() =>
            {
                var path = Path.Combine(Path.GetTempPath(),
                    "pynavis_output_" + Guid.NewGuid().ToString("N") + ".txt");
                try
                {
                    var window = new OutputWindow("t");
                    window.AppendText("saved line\n", false);
                    var failures = Record(window);

                    Assert.True(window.SaveTo(path));
                    Assert.Equal("saved line\n", File.ReadAllText(path));
                    Assert.Empty(failures);
                }
                finally { File.Delete(path); }
            });
        }

        [Fact]
        public void A_Failed_Save_Is_Reported_Not_Thrown()
        {
            OnSta(() =>
            {
                var window = new OutputWindow("t");
                window.AppendText("hello\n", false);
                var failures = Record(window);
                var path = Path.Combine(Path.GetTempPath(),
                    "pynavis_missing_" + Guid.NewGuid().ToString("N"), "out.txt");

                Assert.False(window.SaveTo(path));

                var failure = Assert.Single(failures);
                Assert.Equal("Could not save the output", failure.Item1);
                Assert.False(string.IsNullOrEmpty(failure.Item2));
            });
        }

        private static List<Tuple<string, string>> Record(OutputWindow window)
        {
            var failures = new List<Tuple<string, string>>();
            window.ReportFailure = (message, detail) => failures.Add(Tuple.Create(message, detail));
            return failures;
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
