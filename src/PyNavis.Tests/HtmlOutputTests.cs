using PyNavis.Runtime.Output;
using PyNavis.Runtime.Output.Html;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The pure parts of the HTML output pipeline - page skeleton, script
    /// payload escaping, element-link registry. The WebView2 window is a thin shell
    /// over these.
    /// </summary>
    public class HtmlPageTests
    {
        [Fact]
        public void ElementLink_IsKeyboardReachable()
        {
            var html = HtmlPage.ElementLinkHtml(7, "Show");

            // An anchor with no href is skipped by Tab entirely, which made
            // every element link mouse-only.
            Assert.Contains("href=", html);
            Assert.Contains("tabindex=\"0\"", html);
            Assert.Contains("data-pynavis-el=\"7\"", html);
        }

        [Fact]
        public void Skeleton_ShowsKeyboardFocus_AndUsesTheQuietScrollbar()
        {
            var html = HtmlPage.Skeleton(OutputPalette.Light, "#0067C0");

            Assert.Contains(":focus-visible", html);
            Assert.Contains("scrollbar { width: 10px", html);
            Assert.Contains("--err-wash", html);
        }

        public void Skeleton_CarriesPaletteColors_AsCssVariables()
        {
            var html = HtmlPage.Skeleton(OutputPalette.Dark, "#0067C0");

            Assert.Contains("--bg: #1E1E1E", html);      // Dark background 30,30,30
            Assert.Contains("--fg: #DCDCDC", html);      // Dark foreground 220,220,220
            Assert.Contains("--accent: #0067C0", html);
            Assert.Contains("pynavisAppend", html);       // the JS append entry point
            Assert.Contains("chrome.webview.postMessage", html); // element-link bridge
        }

        [Fact]
        public void Skeleton_IsV2_SolidSurfaces_NoGlassGradientOrRestFade()
        {
            // Design system v2: solid surfaces, no acrylic, no gradient, no
            // hover-reveal ghosting. The AI-slop tells are gone.
            var html = HtmlPage.Skeleton(OutputPalette.Dark, "#0067C0");

            Assert.DoesNotContain("backdrop-filter", html);   // no frosted glass
            Assert.DoesNotContain("linear-gradient", html);   // no gradient fills
            Assert.DoesNotContain("--rest-opacity", html);    // no rest-state ghosting
            Assert.DoesNotContain("translateY", html);        // no card lift on hover
            Assert.Contains("background: var(--bg)", html);   // solid base
            Assert.Contains("tabular-nums", html);            // numbers as tabular text
        }

        [Fact]
        public void Skeleton_StylesCodeBlocks_AndImageFigures()
        {
            var html = HtmlPage.Skeleton(OutputPalette.Dark, "#0067C0");

            Assert.Contains(".pynavis-code", html);
            Assert.Contains("overflow-x: auto", html);
            Assert.Contains("border: 1px solid var(--frame)", html);
            Assert.Contains("figure.pynavis-img", html);
            Assert.Contains("max-width: 100%", html);
            Assert.Contains("figure.pynavis-img figcaption", html);
            Assert.Contains("color: var(--muted)", html);
        }

        [Fact]
        public void AppendedText_GoesInsideCards_NotBareBody()
        {
            var html = HtmlPage.Skeleton(OutputPalette.Light, "#0067C0");

            // JS must create/extend .pynavis-card containers for text runs
            Assert.Contains("pynavis-card", html.Substring(html.IndexOf("<script>")));
        }

        [Fact]
        public void Skeleton_AlwaysPaintsSolidBase_RegardlessOfGlassFlag()
        {
            // The glassBody flag is retained for call-site compatibility but no
            // longer paints a see-through page; v2 is always solid.
            var glass = HtmlPage.Skeleton(OutputPalette.Light, "#0067C0", glassBody: true);
            var solid = HtmlPage.Skeleton(OutputPalette.Light, "#0067C0", glassBody: false);

            Assert.DoesNotContain("transparent", glass);
            Assert.Contains("background: var(--bg)", glass);
            Assert.Contains("background: var(--bg)", solid);
        }

        [Fact]
        public void JsonEscape_HandlesQuotesBackslashesAndNewlines()
        {
            Assert.Equal("say \\\"hi\\\"", HtmlPage.JsonEscape("say \"hi\""));
            Assert.Equal("a\\\\b", HtmlPage.JsonEscape("a\\b"));
            Assert.Equal("line1\\nline2", HtmlPage.JsonEscape("line1\nline2"));
            Assert.Equal("tab\\there", HtmlPage.JsonEscape("tab\there"));
        }

        [Fact]
        public void HtmlEscape_NeutralizesMarkup()
        {
            Assert.Equal("&lt;b&gt;x&amp;y&lt;/b&gt;", HtmlPage.HtmlEscape("<b>x&y</b>"));
        }

        [Fact]
        public void AppendScript_WrapsEscapedPayload()
        {
            var script = HtmlPage.AppendScript("error", "boom \"quoted\"\n");

            Assert.StartsWith("pynavisAppend(", script);
            Assert.Contains("\"kind\":\"error\"", script);
            Assert.Contains("boom \\\"quoted\\\"\\n", script);
        }

        [Fact]
        public void ElementLinkHtml_CarriesIndex_AndEscapesLabel()
        {
            var html = HtmlPage.ElementLinkHtml(7, "Wall <A> & co");

            Assert.Contains("data-pynavis-el=\"7\"", html);
            Assert.Contains("Wall &lt;A&gt; &amp; co", html);
            Assert.DoesNotContain("<A>", html);
        }

        [Fact]
        public void ProgressScript_ClampsFraction()
        {
            Assert.Contains("\"value\":100", HtmlPage.ProgressScript(1.7, "done"));
            Assert.Contains("\"value\":0", HtmlPage.ProgressScript(-0.3, "start"));
            Assert.Contains("\"value\":42", HtmlPage.ProgressScript(0.42, "working"));
        }
    }

    public class ElementRegistryTests
    {
        [Fact]
        public void Register_AssignsSequentialIndexes_AndResolvesBack()
        {
            var registry = new ElementRegistry();
            var a = new object();
            var b = new object();

            Assert.Equal(0, registry.Register(a));
            Assert.Equal(1, registry.Register(b));
            Assert.True(registry.TryResolve(1, out var resolved));
            Assert.Same(b, resolved);
        }

        [Fact]
        public void TryResolve_UnknownIndex_IsFalse()
        {
            var registry = new ElementRegistry();
            Assert.False(registry.TryResolve(0, out _));
            Assert.False(registry.TryResolve(-1, out _));
        }
    }
}
