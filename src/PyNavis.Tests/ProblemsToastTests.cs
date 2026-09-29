using PyNavis.Runtime.Bundles;
using Xunit;

namespace PyNavis.Tests
{
    public class ProblemsToastTests
    {
        // The after-scan toast used to say "N bundle folders were skipped", which was
        // wrong once a bad layout: entry (nothing skipped, one line ignored) started
        // counting into it. The headline counts problems, the body quotes the first.
        [Theory]
        [InlineData(1, "1 bundle problem found")]
        [InlineData(3, "3 bundle problems found")]
        public void ProblemsHeadline_CountsProblems_NotSkippedFolders(int count, string expected)
        {
            Assert.Equal(expected, ExtensionModel.ProblemsHeadline(count));
        }
    }
}
