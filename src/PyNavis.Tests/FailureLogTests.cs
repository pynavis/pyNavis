using PyNavis.Runtime.Ai;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>The last traceback per bundle, so the chat can offer "send the error".</summary>
    public class FailureLogTests
    {
        [Fact]
        public void Record_ThenLastFor_ReturnsIt_CaseInsensitively()
        {
            FailureLog.Record(@"C:\x\AI.extension\T.tab\P.panel\A.pushbutton", "Traceback: boom");

            Assert.Equal("Traceback: boom", FailureLog.LastFor(@"c:\X\ai.extension\t.tab\p.panel\a.pushbutton"));
        }

        [Fact]
        public void Nothing_IsNull_AndClearForgets()
        {
            FailureLog.Record(@"C:\y\B.pushbutton", "err");

            Assert.Null(FailureLog.LastFor(@"C:\y\Unknown.pushbutton"));
            FailureLog.Clear(@"C:\y\B.pushbutton");
            Assert.Null(FailureLog.LastFor(@"C:\y\B.pushbutton"));
        }

        [Fact]
        public void Record_IgnoresBlanks()
        {
            FailureLog.Record(@"C:\z\C.pushbutton", "  ");
            FailureLog.Record(null, "x");

            Assert.Null(FailureLog.LastFor(@"C:\z\C.pushbutton"));
        }
    }
}
