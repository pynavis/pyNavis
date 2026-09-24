using System;
using System.Collections.Generic;

namespace PyNavis.Runtime.Execution
{
    public sealed class BatchResult
    {
        public int Done { get; set; }
        public int Failed { get; set; }
        public bool Cancelled { get; set; }
        public List<string> Errors { get; } = new List<string>();
    }

    /// <summary>
    /// Runs a bulk operation in chunks so the UI can breathe: progress is
    /// reported on chunk boundaries and cancellation is checked there too.
    /// Deliberately pure - no Navisworks, no WPF - because the interesting
    /// rules (keep going after a failure, stop cleanly on cancel, report what
    /// actually finished) are what need testing.
    ///
    /// The Navisworks API is main-thread only, so callers pump the dispatcher
    /// between chunks rather than moving this to a worker thread.
    /// </summary>
    public static class BatchRunner
    {
        public static BatchResult Run<T>(
            IList<T> items,
            Func<T, string> onItem,
            int chunkSize,
            Action<int, int> onProgress,
            Func<bool> cancelled)
        {
            var result = new BatchResult();
            if (items == null || items.Count == 0) return result;
            var chunk = Math.Max(1, chunkSize);

            for (var i = 0; i < items.Count; i++)
            {
                if (i % chunk == 0 && i > 0 && cancelled != null && cancelled())
                {
                    result.Cancelled = true;
                    return result;
                }

                string error;
                try
                {
                    error = onItem(items[i]);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                if (error == null) result.Done++;
                else
                {
                    result.Failed++;
                    result.Errors.Add(error);
                }

                var processed = i + 1;
                if (onProgress != null && (processed % chunk == 0 || processed == items.Count))
                    onProgress(processed, items.Count);
            }
            return result;
        }
    }
}
