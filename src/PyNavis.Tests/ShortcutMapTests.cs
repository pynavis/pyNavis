using System.Collections.Generic;
using System.Linq;
using PyNavis.Runtime.Bundles;
using PyNavis.Runtime.Input;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>Merge rules for the chord table: author defaults, user overrides, conflicts.</summary>
    public class ShortcutMapTests
    {
        private static PushButtonModel Button(string key, string shortcut = null) =>
            new PushButtonModel { BundleKey = key, Title = key, ScriptPath = key, Shortcut = shortcut };

        private static Chord C(string text)
        {
            Assert.True(ChordParser.TryParse(text, true, out var chord, out var error), error);
            return chord;
        }

        [Fact]
        public void AuthorDefault_Binds_AndResolvesBothWays()
        {
            var b = Button("t/p/mem", "Ctrl+Shift+M");
            var map = ShortcutMap.Build(new[] { b }, new Dictionary<string, string>(), false);

            Assert.True(map.TryGetButton(C("Ctrl+Shift+M"), out var hit));
            Assert.Same(b, hit);
            Assert.Equal(C("Ctrl+Shift+M"), map.BindingFor(b));
            Assert.Empty(map.Problems);
        }

        [Fact]
        public void UserOverride_Wins_AndNullDisables()
        {
            var mem = Button("t/p/mem", "Ctrl+Shift+M");
            var rec = Button("t/p/rec", "Ctrl+Shift+R");
            var overrides = new Dictionary<string, string>
            {
                ["t/p/mem"] = null,               // disable the default
                ["t/p/rec"] = "Ctrl+Alt+R",       // rebind
            };
            var map = ShortcutMap.Build(new[] { mem, rec }, overrides, false);

            Assert.False(map.TryGetButton(C("Ctrl+Shift+M"), out _));
            Assert.Null(map.BindingFor(mem));
            Assert.False(map.TryGetButton(C("Ctrl+Shift+R"), out _));
            Assert.True(map.TryGetButton(C("Ctrl+Alt+R"), out var hit));
            Assert.Same(rec, hit);
        }

        [Fact]
        public void UserBinding_ForToolWithoutDefault_Works()
        {
            var b = Button("t/p/console");
            var map = ShortcutMap.Build(new[] { b },
                new Dictionary<string, string> { ["t/p/console"] = "Ctrl+Alt+C" }, false);
            Assert.True(map.TryGetButton(C("Ctrl+Alt+C"), out _));
        }

        [Fact]
        public void Conflict_FirstWins_LoserReported()
        {
            var first = Button("t/p/a", "Ctrl+Shift+X");
            var second = Button("t/p/b", "Ctrl+Shift+X");
            var map = ShortcutMap.Build(new[] { first, second }, new Dictionary<string, string>(), false);

            Assert.True(map.TryGetButton(C("Ctrl+Shift+X"), out var hit));
            Assert.Same(first, hit);
            Assert.Null(map.BindingFor(second));
            Assert.Contains(map.Problems, p => p.Contains("t/p/b") && p.Contains("t/p/a"));
        }

        [Fact]
        public void BadStrings_AndUnknownKeys_AreReportedNotThrown()
        {
            var b = Button("t/p/a", "Ctrl+Banana");
            var overrides = new Dictionary<string, string>
            {
                ["t/p/ghost"] = "Ctrl+Alt+G",     // no such tool
                ["t/p/a"] = "NotAChord+",
            };
            var map = ShortcutMap.Build(new[] { b }, overrides, false);

            Assert.Equal(0, map.Count);
            Assert.Contains(map.Problems, p => p.Contains("t/p/ghost"));
            Assert.Contains(map.Problems, p => p.Contains("NotAChord"));
        }

        [Fact]
        public void BarePolicy_IsEnforced_OnBothSources()
        {
            var b = Button("t/p/a", "N");
            var map = ShortcutMap.Build(new[] { b }, new Dictionary<string, string>(), false);
            Assert.Equal(0, map.Count);
            Assert.NotEmpty(map.Problems);

            var permissive = ShortcutMap.Build(new[] { b }, new Dictionary<string, string>(), true);
            Assert.Equal(1, permissive.Count);
        }
    }
}
