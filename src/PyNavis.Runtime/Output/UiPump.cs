using System;
using System.Windows.Threading;

namespace PyNavis.Runtime.Output
{
    /// <summary>
    /// Lets the message loop have a turn from inside a long synchronous script.
    ///
    /// Scripts run ON the Navisworks UI thread (ScriptExecutor.RunScript is called
    /// straight from the ribbon click), so while one is working the thread never
    /// returns to its message loop: nothing repaints, no input is handled, and
    /// Windows paints the white "Not Responding" frame. A user cannot tell that
    /// from a hang - which is exactly how a 4.6-day clash-grouping bug got
    /// reported as "Navisworks hangs".
    ///
    /// An empty callback at Background priority is the barrier: the dispatcher
    /// drains everything above Background first (Loaded, Render, Input), so
    /// waiting on it guarantees a repaint has happened. This is the same trick
    /// ClashGrouperDialog's preview uses; that copy stays where it is because it
    /// is test-pinned and tuned for Cancel latency, not for this.
    /// </summary>
    public static class UiPump
    {
        /// <summary>
        /// Deliberately slower than the preview's 40ms. That number exists so a
        /// Cancel click feels instant; a write phase has no Cancel, so the only
        /// job here is to look alive and keep the bar moving. 200ms is far under
        /// the ~5s at which Windows ghosts a window, and the progress bar's own
        /// 120ms CSS transition smooths the coarser cadence. Five times fewer
        /// pumps also means five times fewer windows for stray input to land in.
        /// </summary>
        public const int IntervalMs = 200;

        /// <summary>
        /// Whether enough time has passed to pump again.
        ///
        /// Wrap-around safe: C# subtraction is unchecked by default, so the
        /// difference of two real Environment.TickCount samples stays correct
        /// across the MaxValue-to-MinValue boundary (TickCount first goes
        /// negative about 24.9 days after boot, full cycle ~49.7 days).
        ///
        /// What it cannot survive is a sentinel. Seed `last` with
        /// Environment.TickCount - IntervalMs, never 0: past 24.9 days of uptime
        /// `now` is negative, `now - 0` is hugely negative, and this would answer
        /// false forever.
        /// </summary>
        public static bool Due(int now, int last) => now - last >= IntervalMs;

        /// <summary>A seed for `last` that leaves the first call Due.</summary>
        public static int Seed() => Environment.TickCount - IntervalMs;

        /// <summary>Drains input, layout and render on the dispatcher's thread.</summary>
        public static void Pump(Dispatcher dispatcher)
        {
            if (dispatcher == null) return;
            try
            {
                dispatcher.Invoke((Action)(() => { }), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                // Never let a repaint courtesy kill the script that asked for it.
                Log.Error("UI pump failed.", ex);
            }
        }
    }
}
