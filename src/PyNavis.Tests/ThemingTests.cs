using PyNavis.Runtime.Output;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// Theming: the pure math (dark-color decision) and the palette quality
    /// bar - both palettes must be genuinely readable, errors included.
    /// </summary>
    public class ThemingTests
    {
        [Fact]
        public void SemanticTokens_ClearWcagAa_AgainstPaper_InBothThemes()
        {
            foreach (var dark in new[] { false, true })
            {
                var t = Runtime.Forms.DesignSystem.Tokens.For(dark);
                var pairs = new (string name, System.Windows.Media.Color color)[]
                {
                    ("success", t.Success), ("info", t.Info),
                    ("warning", t.Warning), ("error", t.Error),
                };
                foreach (var (name, c) in pairs)
                {
                    var ratio = ThemeMath.ContrastRatio(
                        c.R, c.G, c.B, t.Paper.R, t.Paper.G, t.Paper.B);
                    Assert.True(ratio >= 4.5,
                        $"{name} on {(dark ? "dark" : "light")} paper is only {ratio:F2}:1");
                }
            }
        }

        [Theory]
        [InlineData(255, 255, 255, false)] // white
        [InlineData(240, 240, 240, false)] // classic light chrome
        [InlineData(0, 0, 0, true)]        // black
        [InlineData(43, 43, 43, true)]     // typical dark chrome
        [InlineData(59, 68, 83, true)]     // dark blue-gray
        public void IsDarkColor_ClassifiesRealChromeColors(int r, int g, int b, bool expectDark)
        {
            Assert.Equal(expectDark, ThemeMath.IsDarkColor(r, g, b));
        }

        [Fact]
        public void Palettes_MatchTheirTheme()
        {
            Assert.False(ThemeMath.IsDarkColor(
                OutputPalette.Light.BackgroundR, OutputPalette.Light.BackgroundG, OutputPalette.Light.BackgroundB));
            Assert.True(ThemeMath.IsDarkColor(
                OutputPalette.Dark.BackgroundR, OutputPalette.Dark.BackgroundG, OutputPalette.Dark.BackgroundB));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ForegroundAndError_AreReadable_OnTheirBackground(bool dark)
        {
            // WCAG AA for normal text: contrast ratio >= 4.5. Applies to tracebacks too.
            var p = dark ? OutputPalette.Dark : OutputPalette.Light;

            var fg = ThemeMath.ContrastRatio(
                p.ForegroundR, p.ForegroundG, p.ForegroundB, p.BackgroundR, p.BackgroundG, p.BackgroundB);
            var err = ThemeMath.ContrastRatio(
                p.ErrorR, p.ErrorG, p.ErrorB, p.BackgroundR, p.BackgroundG, p.BackgroundB);

            Assert.True(fg >= 4.5, $"foreground contrast {fg:F2} < 4.5");
            Assert.True(err >= 4.5, $"error contrast {err:F2} < 4.5");
        }

        [Fact]
        public void ThemeOverride_FromConfig_Wins()
        {
            Assert.True(PyNavisTheme.ResolveIsDark("dark", () => false));
            Assert.False(PyNavisTheme.ResolveIsDark("light", () => true));
            Assert.True(PyNavisTheme.ResolveIsDark(null, () => true));
            Assert.False(PyNavisTheme.ResolveIsDark("nonsense", () => false));
        }
    }

    /// <summary>Find-in-output search logic (pure; the window only jumps between hits).</summary>
    public class TextSearchTests
    {
        [Fact]
        public void FindAll_ReturnsOffsets_CaseInsensitive()
        {
            var hits = TextSearch.FindAll("Error: red\nerror again", "error");
            Assert.Equal(new[] { 0, 11 }, hits);
        }

        [Fact]
        public void FindAll_EmptyQueryOrText_YieldsNothing()
        {
            Assert.Empty(TextSearch.FindAll("abc", ""));
            Assert.Empty(TextSearch.FindAll("", "x"));
            Assert.Empty(TextSearch.FindAll("abc", null));
        }

        [Fact]
        public void NextAfter_CyclesThroughHits_AndWraps()
        {
            var hits = new[] { 3, 10, 25 };
            Assert.Equal(10, TextSearch.NextAfter(hits, 3));
            Assert.Equal(25, TextSearch.NextAfter(hits, 10));
            Assert.Equal(3, TextSearch.NextAfter(hits, 25));  // wrap
            Assert.Equal(3, TextSearch.NextAfter(hits, -1));  // before first
        }
    }
}
