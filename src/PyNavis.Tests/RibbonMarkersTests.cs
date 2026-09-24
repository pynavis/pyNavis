using System;
using System.IO;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PyNavis.Runtime.Config;
using PyNavis.Runtime.Ribbon;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The two hints a button can carry: the caption marker for a resolved chord and
    /// the dot for a bundle with a config.py. The text half is pure; the bitmap half
    /// is checked through its size and DPI, because the DPI is what makes AdWindows
    /// draw a 96px glyph at the size of the slot it is in.
    /// </summary>
    public class RibbonMarkersTests : IDisposable
    {
        public RibbonMarkersTests() => RibbonMarkers.ResetForTests();
        public void Dispose() => RibbonMarkers.ResetForTests();

        private static PyNavisConfig ConfigFrom(string json)
        {
            var path = Path.Combine(Path.GetTempPath(), "pynavis-ribbon-" + Guid.NewGuid() + ".json");
            File.WriteAllText(path, json);
            try { return PyNavisConfig.Load(path); }
            finally { File.Delete(path); }
        }

        // ---- defaults -------------------------------------------------------

        [Fact]
        public void AFreshInstall_ShowsBothHints_WithoutAnyConfiguration()
        {
            var config = ConfigFrom("{}");

            Assert.True(config.RibbonConfigDot);
            Assert.Equal("\u25CF", config.RibbonShortcutMarker);
        }

        [Fact]
        public void AConfigWithNoRibbonSection_LeavesBothAtTheirDefaults()
        {
            var config = ConfigFrom("{\"theme\": \"dark\", \"panes\": {\"extraSlots\": 2}}");

            Assert.True(config.RibbonConfigDot);
            Assert.Equal("\u25CF", config.RibbonShortcutMarker);
        }

        // ---- reading the two keys -------------------------------------------

        [Fact]
        public void BothKeys_AreReadFromTheRibbonSection()
        {
            var config = ConfigFrom(
                "{\"ribbon\": {\"configDot\": false, \"shortcutMarker\": \"*\"}}");

            Assert.False(config.RibbonConfigDot);
            Assert.Equal("*", config.RibbonShortcutMarker);
        }

        [Fact]
        public void AnEmptyMarker_Survives_BecauseEmptyIsHowTheMarkerIsTurnedOff()
        {
            // The obvious bug here is treating "" as "missing" and handing back the
            // default bullet, which would make the documented off switch do nothing.
            var config = ConfigFrom("{\"ribbon\": {\"shortcutMarker\": \"\"}}");

            Assert.Equal("", config.RibbonShortcutMarker);

            RibbonMarkers.Configure(config);
            Assert.Equal("Memorize", RibbonMarkers.WithShortcutMarker("Memorize"));
        }

        [Fact]
        public void AMarkerOfTheWrongType_IsIgnoredRatherThanCrashingTheRibbon()
        {
            var config = ConfigFrom("{\"ribbon\": {\"configDot\": \"yes\", \"shortcutMarker\": 7}}");

            Assert.True(config.RibbonConfigDot);
            Assert.Equal("\u25CF", config.RibbonShortcutMarker);
        }

        // ---- what the caption becomes ---------------------------------------

        [Fact]
        public void TheMarker_JoinsTheCaption_SeparatedByASpace()
        {
            Assert.Equal("Memorize \u25CF", RibbonMarkers.WithShortcutMarker("Memorize"));
        }

        [Fact]
        public void OnATwoLineCaption_TheMarkerJoinsTheLastLine_NotTheFirst()
        {
            // A large button's RibbonTitle carries its own newline to wrap a long name.
            // Appending past that would strand the marker beside the first word.
            Assert.Equal("Smart Clash\nGrouper \u25CF",
                RibbonMarkers.WithShortcutMarker("Smart Clash\nGrouper"));
        }

        [Fact]
        public void AMarkerStartingWithANewline_TakesItsOwnLine()
        {
            // This is how a caller asks for the marker UNDER the name rather than
            // beside it, without needing a second setting to say so.
            var config = ConfigFrom("{\"ribbon\": {\"shortcutMarker\": \"\\n\u25CF\"}}");
            RibbonMarkers.Configure(config);

            Assert.Equal("Memorize\n\u25CF", RibbonMarkers.WithShortcutMarker("Memorize"));
        }

        [Fact]
        public void AnEmptyOrNullCaption_IsLeftAlone()
        {
            Assert.Equal("", RibbonMarkers.WithShortcutMarker(""));
            Assert.Null(RibbonMarkers.WithShortcutMarker(null));
        }

        [Fact]
        public void Configure_WithNoConfig_FallsBackToTheDefaults()
        {
            RibbonMarkers.Configure(null);

            Assert.True(RibbonMarkers.ConfigDot);
            Assert.Equal("Memorize \u25CF", RibbonMarkers.WithShortcutMarker("Memorize"));
        }

        // ---- the dot ---------------------------------------------------------

        private static BitmapSource Plain(int size, double dpi)
        {
            var stride = (size * PixelFormats.Pbgra32.BitsPerPixel + 7) / 8;
            return BitmapSource.Create(size, size, dpi, dpi, PixelFormats.Pbgra32,
                null, new byte[stride * size], stride);
        }

        private static void OnStaThread(Action body)
        {
            Exception failure = null;
            var thread = new Thread(() => { try { body(); } catch (Exception ex) { failure = ex; } });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        [Fact]
        public void TheDot_KeepsThePixelSizeAndTheDpi_SoTheSlotSizeSurvives()
        {
            // RibbonIcons.Load re-stamps DPI so a 96px bitmap draws at 32 logical
            // units. RenderTargetBitmap always renders at 96 DPI, so losing the
            // stamp here would put full-size art back on a ribbon button.
            OnStaThread(() =>
            {
                var source = Plain(96, 288);      // 96px art stamped for a 32-unit slot
                var dotted = RibbonMarkers.WithConfigDot(source);

                Assert.NotNull(dotted);
                Assert.Equal(96, dotted.PixelWidth);
                Assert.Equal(96, dotted.PixelHeight);
                Assert.Equal(288, dotted.DpiX, 3);
                Assert.Equal(288, dotted.DpiY, 3);
            });
        }

        [Fact]
        public void TheDot_ActuallyMarksTheBitmap_InItsBottomRightCorner()
        {
            OnStaThread(() =>
            {
                var source = Plain(96, 96);
                var dotted = RibbonMarkers.WithConfigDot(source);

                var stride = (96 * PixelFormats.Pbgra32.BitsPerPixel + 7) / 8;
                var pixels = new byte[stride * 96];
                dotted.CopyPixels(pixels, stride, 0);

                int Alpha(int x, int y) => pixels[y * stride + x * 4 + 3];

                // Source was fully transparent, so any opacity is the dot.
                Assert.True(Alpha(80, 80) > 0, "no dot in the bottom right corner");
                Assert.Equal(0, Alpha(10, 10));   // top left untouched
            });
        }

        [Fact]
        public void ANullBitmap_StaysNull_SoAnIconlessButtonIsNotADotOnItsOwn()
        {
            Assert.Null(RibbonMarkers.WithConfigDot(null));
        }
    }
}
