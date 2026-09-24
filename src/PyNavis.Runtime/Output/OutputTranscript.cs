using System;
using System.Collections.Generic;
using System.Text;
using PyNavis.Runtime.Forms;
using PyNavis.Runtime.Output.Html;

namespace PyNavis.Runtime.Output
{
    /// <summary>
    /// Records every output fragment (text / error / html) in call order,
    /// independent of any live window. OutputWindowWriter feeds this on every
    /// Write/WriteError/WriteHtml; Transcript replays the recording into a
    /// standalone HTML page - used by output.save() and, because it never
    /// touches a WPF window, safe to exercise directly from headless tests
    /// (constructing OutputWindow/HtmlOutputWindow off the UI thread throws).
    /// </summary>
    public class OutputTranscript
    {
        private readonly List<(string Kind, string Content)> _fragments = new List<(string, string)>();

        /// <summary>
        /// Appends one fragment; kind is "text", "error", or "html". Adjacent
        /// same-kind "text"/"error" fragments are coalesced into the previous
        /// one instead of appended as a new entry - IronPython's stdout arrives
        /// through TextWriterStream one character (or arbitrary chunk) at a
        /// time, and without this a plain print() would otherwise turn into one
        /// &lt;pre&gt; per character in the replayed transcript. This mirrors
        /// what the live window's JS already does (pynavisAppend extends the
        /// current .pynavis-card's &lt;pre&gt; for consecutive same-kind runs).
        /// html fragments are never merged, in either direction.
        /// </summary>
        public void Record(string kind, string content)
        {
            if ((kind == "text" || kind == "error") && _fragments.Count > 0)
            {
                var last = _fragments[_fragments.Count - 1];
                if (last.Kind == kind)
                {
                    _fragments[_fragments.Count - 1] = (kind, last.Content + content);
                    return;
                }
            }
            _fragments.Add((kind, content));
        }

        /// <summary>
        /// A standalone HTML page: the same skeleton (CSS + palette) the live
        /// window renders, followed by every recorded fragment replayed as
        /// static markup in call order. Text fragments are escaped into a
        /// <c>&lt;pre&gt;</c>, error fragments into an error-styled div, and
        /// html fragments are inserted verbatim (they were already markup).
        /// Errors replay as a <c>&lt;pre&gt;</c> too, not a div: a traceback is
        /// whitespace-significant, and a div's normal white-space handling
        /// collapsed every frame onto one line.
        /// </summary>
        public string Transcript
        {
            get
            {
                var accent = FluentChrome.AccentColor();
                var accentHex = $"#{accent.R:X2}{accent.G:X2}{accent.B:X2}";
                var skeleton = HtmlPage.Skeleton(OutputPalette.For(PyNavisTheme.IsDark), accentHex);

                var body = new StringBuilder();
                foreach (var fragment in _fragments)
                {
                    switch (fragment.Kind)
                    {
                        case "html":
                            body.Append(fragment.Content);
                            break;
                        case "error":
                            body.Append("<pre class=\"pynavis-error\">")
                                .Append(HtmlPage.HtmlEscape(fragment.Content))
                                .Append("</pre>");
                            break;
                        default: // "text"
                            body.Append("<pre class=\"pynavis-text\">")
                                .Append(HtmlPage.HtmlEscape(fragment.Content))
                                .Append("</pre>");
                            break;
                    }
                }

                var insertAt = skeleton.LastIndexOf("</body>", StringComparison.Ordinal);
                return skeleton.Substring(0, insertAt) + body + skeleton.Substring(insertAt);
            }
        }
    }
}
