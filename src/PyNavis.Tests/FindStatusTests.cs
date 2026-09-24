using PyNavis.Runtime.Output;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The find field's readout. Searching used to give no feedback at all: a
    /// query with no hits silently rewound to the top, which reads as broken.
    /// </summary>
    public class FindStatusTests
    {
        private static int[] Hits(string text, string query) => TextSearch.FindAll(text, query);

        [Fact]
        public void EmptyQuery_SaysNothing()
        {
            Assert.Equal("", TextSearch.StatusFor(new int[0], -1, ""));
            Assert.Equal("", TextSearch.StatusFor(null, -1, null));
        }

        [Fact]
        public void NoHits_SaysSo()
        {
            var hits = Hits("clash report", "beam");
            Assert.Equal("no matches", TextSearch.StatusFor(hits, -1, "beam"));
        }

        [Fact]
        public void BeforeSteppingIn_ReportsTheTotal()
        {
            var hits = Hits("beam beam beam", "beam");
            Assert.Equal("3 matches", TextSearch.StatusFor(hits, -1, "beam"));
        }

        [Fact]
        public void WhileSteppingThrough_ReportsThePosition()
        {
            var text = "beam duct beam duct beam";
            var hits = Hits(text, "beam");

            Assert.Equal("1 of 3", TextSearch.StatusFor(hits, hits[0], "beam"));
            Assert.Equal("2 of 3", TextSearch.StatusFor(hits, hits[1], "beam"));
            Assert.Equal("3 of 3", TextSearch.StatusFor(hits, hits[2], "beam"));
        }
    }
}
