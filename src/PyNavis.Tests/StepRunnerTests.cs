using System;
using System.Collections.Generic;
using PyNavis.Runtime;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// Boot and Reload used to run every stage inside ONE try/catch, so a single throwing
    /// bundle left a half-built ribbon and silently skipped hooks, the context gate and the
    /// shortcut hook. Each stage now fails alone, and the failures are named for the user.
    /// </summary>
    public class StepRunnerTests
    {
        [Fact]
        public void AThrowingStep_DoesNotStopTheStepsAfterIt()
        {
            var ran = new List<string>();
            var steps = new StepRunner();

            steps.Run("first", () => ran.Add("first"));
            steps.Run("ribbon", () => throw new InvalidOperationException("bad bundle"));
            steps.Run("hooks", () => ran.Add("hooks"));

            Assert.Equal(new[] { "first", "hooks" }, ran);
            Assert.Equal(new[] { "ribbon" }, steps.Failed);
        }

        [Fact]
        public void NoFailures_MeansNoSummary()
        {
            var steps = new StepRunner();
            steps.Run("only", () => { });

            Assert.Empty(steps.Failed);
            Assert.Null(steps.Summary());
        }

        [Fact]
        public void Summary_NamesWhatFailed_AndWhereTheLogIs()
        {
            var steps = new StepRunner();
            steps.Run("ribbon", () => throw new Exception("x"));
            steps.Run("hooks", () => throw new Exception("y"));

            var summary = steps.Summary();

            Assert.Contains("ribbon", summary);
            Assert.Contains("hooks", summary);
            Assert.Contains(Log.LogDir, summary);
        }

        [Fact]
        public void Each_RunsEveryItem_EvenWhenOneThrows()
        {
            var seen = new List<int>();
            var steps = new StepRunner();

            steps.Each(new[] { 1, 2, 3 }, n => "item " + n, n =>
            {
                if (n == 2) throw new Exception("boom");
                seen.Add(n);
            });

            Assert.Equal(new[] { 1, 3 }, seen);
            Assert.Equal(new[] { "item 2" }, steps.Failed);
        }
    }
}
