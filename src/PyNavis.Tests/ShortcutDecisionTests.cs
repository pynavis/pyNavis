using PyNavis.Runtime.Input;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>The chord dispatch guards, pure: keydown only, no repeats, main window
    /// foreground, focus outside text inputs.</summary>
    public class ShortcutDecisionTests
    {
        [Theory]
        [InlineData(true, false, true, false, true)]    // clean keydown in main window -> dispatch
        [InlineData(false, false, true, false, false)]  // keyup
        [InlineData(true, true, true, false, false)]    // auto-repeat
        [InlineData(true, false, false, false, false)]  // dialog or other window foreground
        [InlineData(true, false, true, true, false)]    // typing in a text field
        public void ShouldDispatch_AppliesEveryGuard(
            bool down, bool repeat, bool mainFg, bool textFocus, bool expected)
        {
            Assert.Equal(expected,
                ShortcutManager.ShouldDispatch(down, repeat, mainFg, textFocus));
        }
    }
}
