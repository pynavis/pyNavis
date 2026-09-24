using System.Collections.Generic;
using System.Linq;
using PyNavis.Runtime.Execution;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The chunked bulk-apply core: progress on chunk boundaries, per-item
    /// failures that do not stop the run, and a cancel that is honored between
    /// chunks. Pure, so none of this needs Navisworks or a UI thread.
    /// </summary>
    public class BatchRunnerTests
    {
        private static List<int> Items(int n) => Enumerable.Range(1, n).ToList();

        [Fact]
        public void RunsEveryItem_ReportingProgressOnChunkBoundaries()
        {
            var seen = new List<(int done, int total)>();
            var result = BatchRunner.Run(Items(10), _ => null, 3,
                (done, total) => seen.Add((done, total)), () => false);

            Assert.Equal(10, result.Done);
            Assert.Equal(0, result.Failed);
            Assert.False(result.Cancelled);
            Assert.Equal(new[] { (3, 10), (6, 10), (9, 10), (10, 10) }, seen);
        }

        [Fact]
        public void FailedItem_IsCounted_AndTheRunContinues()
        {
            var result = BatchRunner.Run(Items(5),
                i => i == 3 ? "boom" : null, 10, null, () => false);

            Assert.Equal(4, result.Done);
            Assert.Equal(1, result.Failed);
            Assert.Equal(new[] { "boom" }, result.Errors);
        }

        [Fact]
        public void Cancel_StopsBetweenChunks_AndReportsWhatFinished()
        {
            var processed = 0;
            var result = BatchRunner.Run(Items(20),
                _ => { processed++; return null; }, 4, null, () => processed >= 8);

            Assert.True(result.Cancelled);
            Assert.Equal(8, result.Done);
            Assert.Equal(8, processed);
        }

        [Fact]
        public void EmptyList_DoesNothing_AndReportsNoProgress()
        {
            var calls = 0;
            var result = BatchRunner.Run(new List<int>(), _ => null, 5,
                (d, t) => calls++, () => false);

            Assert.Equal(0, result.Done);
            Assert.False(result.Cancelled);
            Assert.Equal(0, calls);
        }

        [Fact]
        public void ThrowingItem_IsRecordedAsAFailure_NotAnEscape()
        {
            var result = BatchRunner.Run(Items(3),
                i => i == 2 ? throw new System.InvalidOperationException("nope") : null,
                10, null, () => false);

            Assert.Equal(2, result.Done);
            Assert.Equal(1, result.Failed);
            Assert.Contains("nope", result.Errors[0]);
        }
    }
}
