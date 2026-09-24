using System.IO;
using System.Text.RegularExpressions;
using PyNavis.Runtime.Output;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// OutputTranscript records fragments independent of any live window, so it
    /// can be exercised directly here. OutputWindowWriter opens a real WPF
    /// window (OutputWindow/HtmlOutputWindow) the first time it is written to,
    /// which throws off the UI thread (xunit runs MTA) - constructing THIS class
    /// never touches a window, so it is the headless-safe seam for the same
    /// "skeleton + fragments in order" contract that OutputWindowWriter.Transcript
    /// exposes.
    /// </summary>
    public class OutputTranscriptTests
    {
        [Fact]
        public void Transcript_Replays_Text_And_Html_In_Order()
        {
            var transcript = new OutputTranscript();
            transcript.Record("text", "plain line\n");
            transcript.Record("html", "<div class=\"x\">rich</div>");
            var page = transcript.Transcript;

            Assert.Contains("plain line", page);
            Assert.Contains("<div class=\"x\">rich</div>", page);
            Assert.True(page.IndexOf("plain line") < page.IndexOf("rich"));
            Assert.StartsWith("<!DOCTYPE html>", page.TrimStart());
        }

        [Fact]
        public void Transcript_Escapes_TextFragments()
        {
            var transcript = new OutputTranscript();
            transcript.Record("text", "<script>alert(1)</script>");
            var page = transcript.Transcript;

            Assert.DoesNotContain("<script>alert(1)</script>", page);
            Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", page);
            Assert.Contains("<pre class=\"pynavis-text\">", page);
        }

        [Fact]
        public void Transcript_Wraps_ErrorFragments_InErrorStyledPre()
        {
            var transcript = new OutputTranscript();
            transcript.Record("error", "boom & <bang>");
            var page = transcript.Transcript;

            Assert.Contains("<pre class=\"pynavis-error\">boom &amp; &lt;bang&gt;</pre>", page);
        }

        [Fact]
        public void Transcript_Keeps_ErrorLineBreaks_Intact()
        {
            // A traceback replayed into a <div> renders with white-space: normal,
            // which collapses every frame onto one line. <pre> is the whole point.
            var transcript = new OutputTranscript();
            transcript.Record("error", "Traceback:\n  File a.py, line 1\nBoom\n");
            var page = transcript.Transcript;

            Assert.Contains("<pre class=\"pynavis-error\">Traceback:\n  File a.py, line 1\nBoom\n</pre>", page);
            Assert.DoesNotContain("<div class=\"pynavis-error\">", page);
        }

        [Fact]
        public void Transcript_Coalesces_AdjacentSameKindTextRecords_IntoOnePre()
        {
            // IronPython's stdout arrives via TextWriterStream, which (absent the
            // Write(char[], int, int) override) had the net48 TextWriter base loop
            // Write(char) once per character - one Record("text", <1 char>) call
            // per character of a plain print(). Coalescing at Record time keeps
            // a contiguous run of same-kind text as a single <pre>, however many
            // calls produced it.
            var transcript = new OutputTranscript();
            transcript.Record("text", "a");
            transcript.Record("text", "b");
            transcript.Record("text", "c\n");
            var page = transcript.Transcript;

            // Match the actual fragment tag, not the JS skeleton's own comment
            // text ("...extends the current glass card's <pre>"), which also
            // contains the bare substring "<pre".
            Assert.Single(Regex.Matches(page, "<pre class=\"pynavis-text\""));
            Assert.Contains("<pre class=\"pynavis-text\">abc\n</pre>", page);
        }

        [Fact]
        public void Transcript_Interleaved_TextAndHtml_KeepsSeparatePreBlocks_InOrder()
        {
            var transcript = new OutputTranscript();
            transcript.Record("text", "x");
            transcript.Record("html", "<div/>");
            transcript.Record("text", "y");
            var page = transcript.Transcript;

            // html never merges with surrounding text runs in either direction,
            // so this is two <pre> blocks around the div, not one.
            Assert.Equal(2, Regex.Matches(page, "<pre class=\"pynavis-text\"").Count);
            var xAt = page.IndexOf(">x<", System.StringComparison.Ordinal);
            var divAt = page.IndexOf("<div/>", System.StringComparison.Ordinal);
            var yAt = page.IndexOf(">y<", System.StringComparison.Ordinal);
            Assert.True(xAt >= 0 && divAt > xAt && yAt > divAt, page);
        }

        [Fact]
        public void OutputWindowWriter_Write_CharArray_RecordsOneBulkFragment()
        {
            // The regression itself: TextWriterStream calls this exact overload
            // (Write(char[], int, int)), not Write(string)/Write(char). Before the
            // override existed, the base class's default implementation looped
            // Write(char) per element.
            var writer = new OutputWindowWriter("T");
            var chars = new[] { 'h', 'i' };
            try
            {
                writer.Write(chars, 0, chars.Length);
            }
            catch (System.InvalidOperationException)
            {
                // Recording happens before the writer ever touches the live
                // window, so this is fine to ignore: off an STA thread (xunit's
                // default), constructing the live OutputWindow/HtmlOutputWindow
                // throws, but the fragment was already recorded by then.
            }

            var page = writer.Transcript;
            Assert.Contains("hi", page);
            Assert.Single(Regex.Matches(page, "<pre class=\"pynavis-text\""));
        }

        [Fact]
        public void OutputWindowWriter_Transcript_And_SaveTranscript_AreHeadlessSafe()
        {
            // Never calling Write/WriteHtml/WriteError here - those open a live
            // window (Window getter), which is not headless-safe. Construction,
            // Transcript and SaveTranscript on an untouched writer must be.
            var writer = new OutputWindowWriter("T");
            var path = Path.Combine(Path.GetTempPath(), "pynavis-transcript-" + System.Guid.NewGuid() + ".html");
            try
            {
                writer.SaveTranscript(path);
                var saved = File.ReadAllText(path);
                Assert.StartsWith("<!DOCTYPE html>", saved.TrimStart());
                Assert.Equal(writer.Transcript, saved);

                // No window ever opened, so SetTitle must not throw - and it must
                // not be silently thrown away either (see the pending-title test).
                writer.SetTitle("New Title");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void OutputWindowWriter_SetTitle_BeforeFirstPrint_IsHeldForTheWindow()
        {
            // set_title() ahead of the first print used to hit a null window and
            // vanish; the window then opened under the bundle's default title.
            var writer = new OutputWindowWriter("pyNavis - Tool");
            Assert.Equal("pyNavis - Tool", writer.EffectiveTitle);

            writer.SetTitle("Clash report - 42 results");
            Assert.Equal("Clash report - 42 results", writer.EffectiveTitle);
        }

        [Fact]
        public void OutputWindowWriter_HasOpenWindow_IsFalse_UntilOneMaterializes()
        {
            // What the logger gates its WriteHtml on: reading the Window property
            // CREATES the window, so "is there one" has to be answerable without
            // asking for it.
            var writer = new OutputWindowWriter("T");
            Assert.False(writer.HasOpenWindow);

            writer.SaveTranscript(Path.Combine(Path.GetTempPath(), "pynavis-hasopen-" + System.Guid.NewGuid() + ".html"));
            Assert.False(writer.HasOpenWindow);
        }

        [Theory]
        [InlineData("<figure class=\"pynavis-img\"><img src=\"data:image/png;base64,AAAA\"/></figure>", "[image]\n")]
        [InlineData("  <FIGURE>x</FIGURE>", "[image]\n")]
        [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><path d=\"M0,0\"/></svg>", "[chart]\n")]
        [InlineData("<table class=\"pynavis\"><tr><td>1</td></tr></table>",
                    "<table class=\"pynavis\"><tr><td>1</td></tr></table>\n")]
        public void TextWindow_AppendHtml_Collapses_ImagesAndCharts_ToPlaceholders(string html, string expected)
        {
            // The fallback window has no renderer, so it shows html as source -
            // fine for a table, ruinous for a 2MB base64 image or a wall of SVG
            // path data, which would bury everything the script printed.
            Assert.Equal(expected, OutputWindow.PlainTextFor(html));
        }
    }
}
