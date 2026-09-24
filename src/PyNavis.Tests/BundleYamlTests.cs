using PyNavis.Runtime.Bundles;
using Xunit;

namespace PyNavis.Tests
{
    public class BundleYamlTests
    {
        [Fact]
        public void Parses_KeyValuePairs()
        {
            var d = BundleYaml.Parse("title: Hello World\nauthor: Example Author");
            Assert.Equal("Hello World", d["title"]);
            Assert.Equal("Example Author", d["author"]);
        }

        [Fact]
        public void Ignores_CommentsAndBlankLines()
        {
            var d = BundleYaml.Parse("# a comment\n\ntitle: X\n   # indented comment\n");
            Assert.Single(d);
            Assert.Equal("X", d["title"]);
        }

        [Fact]
        public void Strips_MatchingQuotes()
        {
            var d = BundleYaml.Parse("title: \"Quoted\"\ntooltip: 'Single'");
            Assert.Equal("Quoted", d["title"]);
            Assert.Equal("Single", d["tooltip"]);
        }

        [Fact]
        public void Value_MayContainColons()
        {
            var d = BundleYaml.Parse("tooltip: Usage: click the button");
            Assert.Equal("Usage: click the button", d["tooltip"]);
        }

        [Fact]
        public void Keys_AreLowercased()
        {
            var d = BundleYaml.Parse("Title: X");
            Assert.True(d.ContainsKey("title"));
        }

        [Fact]
        public void EmptyOrNullText_YieldsEmptyDictionary()
        {
            Assert.Empty(BundleYaml.Parse(""));
            Assert.Empty(BundleYaml.Parse(null));
        }
    }
}
