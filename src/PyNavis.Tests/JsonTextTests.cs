using PyNavis;
using Xunit;

namespace PyNavis.Tests
{
    public class JsonTextTests
    {
        [Fact]
        public void PlainText_IsUnchanged()
        {
            Assert.Equal(@"D:\x\y", JsonText.UnescapeString(@"D:\x\y".Replace(@"\", @"\\")));
        }

        [Fact]
        public void DoubledBackslashes_AreCollapsed()
        {
            Assert.Equal(@"D:\Tools\pyNavis\bin\2026\runtime",
                JsonText.UnescapeString(@"D:\\Tools\\pyNavis\\bin\\2026\\runtime"));
        }

        [Fact]
        public void UnicodeEscapes_AreDecoded()
        {
            // PowerShell 5.1 ConvertTo-Json escapes & ' < > as unicode escapes inside
            // strings. Escape sequences are built programmatically ("\" + "u0026") so no
            // tooling layer between here and the compiler can pre-decode them.
            var amp = "\\" + "u0026";
            var apos = "\\" + "u0027";
            Assert.Equal(@"D:\R&D\repo", JsonText.UnescapeString(@"D:\\R" + amp + @"D\\repo"));
            Assert.Equal(@"D:\John's Repo", JsonText.UnescapeString(@"D:\\John" + apos + "s Repo"));
        }

        [Fact]
        public void ForwardSlashEscape_IsDecoded()
        {
            Assert.Equal("a/b", JsonText.UnescapeString(@"a\/b"));
        }

        [Fact]
        public void ControlEscapes_AreDecoded()
        {
            Assert.Equal("a\tb\nc", JsonText.UnescapeString(@"a\tb\nc"));
        }

        [Fact]
        public void LoneTrailingBackslash_DoesNotThrow()
        {
            Assert.Equal("a\\", JsonText.UnescapeString("a\\"));
        }
    }
}
