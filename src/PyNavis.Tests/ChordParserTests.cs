using PyNavis.Runtime.Input;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>Chord string parsing: the pure half of the shortcut layer.</summary>
    public class ChordParserTests
    {
        private static Chord Parse(string text, bool bare = false)
        {
            Assert.True(ChordParser.TryParse(text, bare, out var chord, out var error), error);
            return chord;
        }

        [Theory]
        [InlineData("Ctrl+Shift+M", true, false, true, 0x4D)]
        [InlineData("ctrl+shift+m", true, false, true, 0x4D)]     // case-insensitive
        [InlineData("Shift+Ctrl+M", true, false, true, 0x4D)]     // any order
        [InlineData("Alt+F5", false, true, false, 0x74)]
        [InlineData("Ctrl+Left", true, false, false, 0x25)]
        [InlineData("Ctrl+Shift+Right", true, false, true, 0x27)]
        [InlineData("Ctrl+9", true, false, false, 0x39)]
        [InlineData("Ctrl+Home", true, false, false, 0x24)]
        [InlineData("Ctrl+PgDn", true, false, false, 0x22)]
        public void Parses_ModifiersAndKeys(string text, bool ctrl, bool alt, bool shift, int vk)
        {
            var c = Parse(text);
            Assert.Equal((ctrl, alt, shift, vk), (c.Ctrl, c.Alt, c.Shift, c.VirtualKey));
        }

        [Theory]
        [InlineData("")]
        [InlineData("Ctrl+")]           // no key
        [InlineData("Ctrl+Shift")]      // modifier as key
        [InlineData("Ctrl+M+N")]        // two keys
        [InlineData("Win+M")]           // unsupported modifier
        [InlineData("Ctrl+Banana")]     // unknown key
        public void Rejects_Garbage_WithAnError(string text)
        {
            Assert.False(ChordParser.TryParse(text, true, out _, out var error));
            Assert.False(string.IsNullOrWhiteSpace(error));
        }

        [Fact]
        public void BareKeys_NeedTheOptIn_AndShiftAloneDoesNotCount()
        {
            Assert.False(ChordParser.TryParse("N", false, out _, out var error));
            Assert.Contains("Ctrl or Alt", error);
            Assert.False(ChordParser.TryParse("Shift+N", false, out _, out _));
            Assert.True(ChordParser.TryParse("N", true, out var bare, out _));
            Assert.Equal(0x4E, bare.VirtualKey);
        }

        [Theory]
        [InlineData("shift+ctrl+m", "Ctrl+Shift+M")]
        [InlineData("alt+LEFT", "Alt+Left")]
        [InlineData("Ctrl+pgup", "Ctrl+PgUp")]
        public void ToString_IsCanonical_ForTooltips(string text, string expected)
        {
            Assert.Equal(expected, Parse(text, bare: true).ToString());
        }

        [Fact]
        public void Chords_AreValueEqual_ForDictionaryKeys()
        {
            Assert.Equal(Parse("Ctrl+Shift+M"), Parse("shift+ctrl+M"));
            Assert.NotEqual(Parse("Ctrl+M"), Parse("Ctrl+N"));
        }
    }
}
