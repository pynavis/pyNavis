using System;
using System.IO;
using PyNavis.Runtime.Config;
using PyNavis.Runtime.Ribbon;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The two hints a button can carry in its caption: a marker for a resolved chord
    /// and one for a bundle with a config.py. Both are pure string work.
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
        public void AFreshInstall_ShowsTheShortcutDot_AndNotTheShiftArrow()
        {
            var config = ConfigFrom("{}");

            Assert.Equal("\U0001F845", config.RibbonConfigMarker);
            Assert.False(config.ShowConfigMarker);
            Assert.Equal("\u25CF", config.RibbonShortcutMarker);
            Assert.True(config.ShowShortcutMarker);
        }

        [Fact]
        public void AConfigWithNoRibbonSection_LeavesEverythingAtTheDefaults()
        {
            var config = ConfigFrom("{\"theme\": \"dark\", \"panes\": {\"extraSlots\": 2}}");

            Assert.Equal("\U0001F845", config.RibbonConfigMarker);
            Assert.False(config.ShowConfigMarker);
            Assert.Equal("\u25CF", config.RibbonShortcutMarker);
            Assert.True(config.ShowShortcutMarker);
        }

        // ---- reading the keys -----------------------------------------------

        [Fact]
        public void TheGlyphsAndTheSwitches_AreReadFromTheRibbonSection()
        {
            var config = ConfigFrom(
                "{\"ribbon\": {\"configMarker\": \"S\", \"showConfigMarker\": true, "
                + "\"shortcutMarker\": \"*\", \"showShortcutMarker\": false}}");

            Assert.Equal("S", config.RibbonConfigMarker);
            Assert.True(config.ShowConfigMarker);
            Assert.Equal("*", config.RibbonShortcutMarker);
            Assert.False(config.ShowShortcutMarker);
        }

        [Fact]
        public void AnEmptyGlyph_IsNotAnOffSwitch_ItMeansTheDefaultGlyph()
        {
            // The switch is the boolean. A blank glyph box must not silently hide
            // the hint, so "" reads as the default and the hint stays on.
            var config = ConfigFrom("{\"ribbon\": {\"shortcutMarker\": \"\"}}");

            Assert.Equal("\u25CF", config.RibbonShortcutMarker);
            Assert.True(config.ShowShortcutMarker);

            RibbonMarkers.Configure(config);
            Assert.Equal("Memorize \u25CF", RibbonMarkers.WithShortcutMarker("Memorize"));
        }

        [Fact]
        public void TheSwitch_TurnsTheMarkerOff_AndKeepsTheGlyphForWhenItComesBack()
        {
            var config = ConfigFrom("{\"ribbon\": {\"shortcutMarker\": \"*\", \"showShortcutMarker\": false}}");
            RibbonMarkers.Configure(config);

            Assert.Equal("Memorize", RibbonMarkers.WithShortcutMarker("Memorize"));
            Assert.Equal("*", config.RibbonShortcutMarker);
        }

        [Fact]
        public void AMarkerOfTheWrongType_IsIgnoredRatherThanCrashingTheRibbon()
        {
            var config = ConfigFrom("{\"ribbon\": {\"configMarker\": 3, \"shortcutMarker\": 7, \"showShortcutMarker\": \"yes\"}}");

            Assert.Equal("\U0001F845", config.RibbonConfigMarker);
            Assert.Equal("\u25CF", config.RibbonShortcutMarker);
            Assert.True(config.ShowShortcutMarker);
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

            Assert.Equal("", RibbonMarkers.ConfigMarker);
            Assert.Equal("Memorize \u25CF", RibbonMarkers.WithShortcutMarker("Memorize"));
        }

        // ---- the config marker -----------------------------------------------

        [Fact]
        public void TheConfigMarker_IsOffUntilSwitchedOn_ThenJoinsTheCaptionLikeTheOther()
        {
            Assert.Equal("Purge", RibbonMarkers.WithConfigMarker("Purge"));

            RibbonMarkers.Configure(ConfigFrom("{\"ribbon\": {\"showConfigMarker\": true}}"));
            Assert.Equal("\U0001F845", RibbonMarkers.ConfigMarker);
            Assert.Equal("Purge \U0001F845", RibbonMarkers.WithConfigMarker("Purge"));
            Assert.Equal("Smart Clash\nGrouper \U0001F845", RibbonMarkers.WithConfigMarker("Smart Clash\nGrouper"));
        }

        [Fact]
        public void BothHints_ReadConfigFirstThenShortcut()
        {
            RibbonMarkers.Configure(ConfigFrom("{\"ribbon\": {\"showConfigMarker\": true}}"));
            var caption = RibbonMarkers.WithShortcutMarker(RibbonMarkers.WithConfigMarker("Purge"));
            Assert.Equal("Purge \U0001F845 \u25CF", caption);
        }

        [Fact]
        public void TheOldConfigDotBoolean_IsReadAsTheSwitch()
        {
            RibbonMarkers.Configure(ConfigFrom("{\"ribbon\": {\"configDot\": true}}"));
            Assert.Equal("Purge \U0001F845", RibbonMarkers.WithConfigMarker("Purge"));

            RibbonMarkers.Configure(ConfigFrom("{\"ribbon\": {\"configDot\": false}}"));
            Assert.Equal("Purge", RibbonMarkers.WithConfigMarker("Purge"));

            // The new key wins when both are present.
            RibbonMarkers.Configure(ConfigFrom("{\"ribbon\": {\"configDot\": true, \"showConfigMarker\": false}}"));
            Assert.Equal("Purge", RibbonMarkers.WithConfigMarker("Purge"));
        }
    }
}
