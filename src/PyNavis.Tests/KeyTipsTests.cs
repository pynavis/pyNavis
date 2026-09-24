using System.Collections.Generic;
using System.Linq;
using PyNavis.Runtime.Input;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>Deterministic Alt-keytip assignment: overrides first, then dedupe rules.</summary>
    public class KeyTipsTests
    {
        private static Dictionary<string, string> Assign(params (string id, string title, string tip)[] items) =>
            KeyTips.Assign(items.ToList());

        [Fact]
        public void FirstLetters_ThenTwoLetters_ThenDigits()
        {
            var tips = Assign(("a", "Memorize", null), ("b", "Recall", null), ("c", "Reload", null),
                              ("d", "Rec Room", null));
            Assert.Equal("M", tips["a"]);
            Assert.Equal("R", tips["b"]);
            Assert.Equal("RE", tips["c"]);   // R taken -> first two letters
            Assert.Equal("R2", tips["d"]);   // RE taken too -> letter+digit
        }

        [Fact]
        public void Overrides_ClaimFirst_AndAreUppercased()
        {
            var tips = Assign(("a", "Memorize", "zz"), ("b", "Zoo", null));
            Assert.Equal("ZZ", tips["a"]);
            Assert.Equal("Z", tips["b"]);    // single Z still free
        }

        [Fact]
        public void Deterministic_AcrossCalls()
        {
            (string, string, string)[] items =
                { ("a", "Add", null), ("b", "Alpha", null), ("c", "Apex", null) };
            Assert.Equal(KeyTips.Assign(items.ToList()), KeyTips.Assign(items.ToList()));
        }

        [Fact]
        public void NonLetterTitles_AreSkipped_NotThrown()
        {
            var tips = Assign(("a", "123", null), ("b", "", null), ("c", "Ok", null));
            Assert.False(tips.ContainsKey("a"));
            Assert.False(tips.ContainsKey("b"));
            Assert.Equal("O", tips["c"]);
        }
    }
}
