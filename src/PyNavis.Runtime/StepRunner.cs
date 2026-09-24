using System;
using System.Collections.Generic;

namespace PyNavis.Runtime
{
    /// <summary>
    /// Runs the stages of Boot, Reload and the ribbon build so that each one fails alone:
    /// a throwing stage is logged and named, and the stages after it still run. Without
    /// this, one bad bundle left a half-built ribbon and silently skipped hooks, the
    /// context gate and the shortcut hook, with a single log line as the only trace.
    /// </summary>
    public sealed class StepRunner
    {
        private readonly List<string> _failed = new List<string>();

        /// <summary>Names of the steps that threw, in the order they ran.</summary>
        public IReadOnlyList<string> Failed => _failed;

        public void Run(string name, Action step)
        {
            try
            {
                step();
            }
            catch (Exception ex)
            {
                _failed.Add(name);
                Log.Error($"'{name}' failed - continuing without it.", ex);
            }
        }

        /// <summary>Runs <paramref name="action"/> per item; one throwing item costs only itself.</summary>
        public void Each<T>(IEnumerable<T> items, Func<T, string> describe, Action<T> action)
        {
            foreach (var item in items)
            {
                var current = item;
                Run(describe(current), () => action(current));
            }
        }

        /// <summary>One user-facing line naming what failed and where the log is; null when nothing did.</summary>
        public string Summary()
        {
            if (_failed.Count == 0) return null;
            return "Failed: " + string.Join(", ", _failed) + ". Details are in " + Log.LogDir;
        }
    }
}
