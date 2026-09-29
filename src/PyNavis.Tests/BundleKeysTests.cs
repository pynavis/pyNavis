using PyNavis.Runtime.Bundles;
using Xunit;

namespace PyNavis.Tests
{
    public class BundleKeysTests
    {
        [Fact]
        public void Normalize_DropsTwoOrMoreDigitPrefixes_FromEverySegment()
        {
            Assert.Equal("pyNavis.tab/Selection.panel/Set.stack/Add.pushbutton",
                BundleKeys.Normalize("pyNavis.tab/02_Selection.panel/03_Set.stack/001_Add.pushbutton"));
        }

        [Fact]
        public void Normalize_KeepsSingleDigitPrefixes_AndPlainNames()
        {
            Assert.Equal("T.tab/1_Keep.panel/Plain.pushbutton",
                BundleKeys.Normalize("T.tab/1_Keep.panel/Plain.pushbutton"));
        }

        [Fact]
        public void Normalize_NullOrEmpty_PassesThrough()
        {
            Assert.Null(BundleKeys.Normalize(null));
            Assert.Equal("", BundleKeys.Normalize(""));
        }
    }
}
