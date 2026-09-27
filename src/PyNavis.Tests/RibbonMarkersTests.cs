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
        public void AFreshInstall_ShowsBothHints_WithoutAnyConfiguration()
        {
            var config = ConfigFrom("{}");

            Assert.Equal("\u21E7", config.RibbonConfigMarker);
            Assert.Equal("\u25CF", config.RibbonShortcutMarker);
        }

        [Fact]
        public void AConfigWithNoRibbonSection_LeavesBothAtTheirDefaults()
        {
            var config = ConfigFrom("{\"theme\": \"dark\", \"panes\": {\"extraSlots\": 2}}");

            Assert.Equal("\u21E7", config.RibbonConfigMarker);
            Assert.Equal("\u25CF", config.RibbonShortcutMarker);
        }

        // ---- reading the two keys -------------------------------------------

        [Fact]
        public void BothKeys_AreReadFromTheRibbonSection()
        {
            var config = ConfigFrom(
                "{\"ribbon\": {\"configMarker\": \"S\", \"shortcutMarker\": \"*\"}}");

            Assert.Equal("S", config.RibbonConfigMarker);
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
            var config = ConfigFrom("{\"ribbon\": {\"configMarker\": 3, \"shortcutMarker\": 7}}");

            Assert.Equal("\u21E7", config.RibbonConfigMarker);
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

            Assert.Equal("\u21E7", RibbonMarkers.ConfigMarker);
            Assert.Equal("Memorize \u25CF", RibbonMarkers.WithShortcutMarker("Memorize"));
        }

        // ---- the config marker -----------------------------------------------

        [Fact]
        public void TheConfigMarker_IsTheShiftSymbol_AndJoinsTheCaptionLikeTheOther()
        {
            Assert.Equal("\u21E7", RibbonMarkers.ConfigMarker);
            Assert.Equal("Purge \u21E7", RibbonMarkers.WithConfigMarker("Purge"));
            Assert.Equal("Smart Clash\nGrouper \u21E7", RibbonMarkers.WithConfigMarker("Smart Clash\nGrouper"));
        }

        [Fact]
        public void BothHints_ReadConfigFirstThenShortcut()
        {
            var caption = RibbonMarkers.WithShortcutMarker(RibbonMarkers.WithConfigMarker("Purge"));
            Assert.Equal("Purge \u21E7 \u25CF", caption);
        }

        [Fact]
        public void AnEmptyConfigMarker_TurnsItOff_AndTheOldBooleanStillDoes()
        {
            RibbonMarkers.Configure(ConfigFrom("{\"ribbon\": {\"configMarker\": \"\"}}"));
            Assert.Equal("Purge", RibbonMarkers.WithConfigMarker("Purge"));

            RibbonMarkers.Configure(ConfigFrom("{\"ribbon\": {\"configDot\": false}}"));
            Assert.Equal("Purge", RibbonMarkers.WithConfigMarker("Purge"));

            RibbonMarkers.Configure(ConfigFrom("{\"ribbon\": {\"configDot\": true}}"));
            Assert.Equal("Purge \u21E7", RibbonMarkers.WithConfigMarker("Purge"));
        }
    }
}
