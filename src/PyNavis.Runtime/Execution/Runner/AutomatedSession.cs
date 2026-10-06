using System;
using System.Runtime.CompilerServices;

namespace PyNavis.Runtime.Execution.Runner
{
    /// <summary>
    /// A Navisworks started through the Automation API (what <c>pynavis run</c> drives) has
    /// no one at the screen, so Boot leaves out everything that waits for the ribbon or for
    /// a user: the ribbon itself, hooks, dock panels, shortcuts, upgrade notices and the
    /// update check. Engine setup and extension loading still happen; startup.py is run by
    /// the runner (<see cref="RuntimeHost.EnsureStartupScriptsRan"/>).
    /// </summary>
    internal static class AutomatedSession
    {
        public const string SkipMessage = "Automated session: ribbon, hooks, notices and update check skipped.";

        /// <summary>
        /// True when this Navisworks was started by the Automation API. Field-measured
        /// 2026-10-04: <c>Application.IsAutomated</c> is False in such a session, hidden or
        /// shown, so the deciding signal is the process command line: COM starts an
        /// out-of-process server with <c>-Embedding</c> (or <c>/Embedding</c>), which a
        /// Navisworks a person opens never carries. Both are logged so the log shows which
        /// Boot path ran.
        /// </summary>
        public static bool Detect()
        {
            bool isAutomated;
            try
            {
                isAutomated = ReadIsAutomated();
            }
            catch (Exception ex)
            {
                Log.Error("Could not read Application.IsAutomated; relying on the command line", ex);
                isAutomated = false;
            }
            var args = Environment.GetCommandLineArgs();
            var embedding = StartedByCom(args);
            Log.Info($"Navisworks Application.IsAutomated: {isAutomated}; COM -Embedding: {embedding}; command line: {string.Join(" ", args)}");
            return isAutomated || embedding;
        }

        /// <summary>True when the command line carries COM's <c>-Embedding</c> switch.</summary>
        public static bool StartedByCom(string[] commandLineArgs)
        {
            if (commandLineArgs == null) return false;
            foreach (var arg in commandLineArgs)
            {
                if (arg == null || arg.Length < 2) continue;
                var head = arg[0];
                if (head != '-' && head != '/') continue;
                if (string.Equals(arg.Substring(1), "Embedding", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        // Its own method, so a missing API assembly fails inside Detect's try.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool ReadIsAutomated() => Autodesk.Navisworks.Api.Application.IsAutomated;

        public const string RibbonReadyMessage = "Ribbon became available in the automated session; nothing built.";

        /// <summary>
        /// Queues <paramref name="stages"/> with <paramref name="whenRibbonReady"/>, unless
        /// the session is <paramref name="automated"/>: then only a log line is queued for the
        /// moment the ribbon appears (so the log shows whether a shown window finished
        /// initialising), the stages never run, and the skip is logged. Returns whether the
        /// stages were queued.
        /// </summary>
        public static bool QueueInteractiveStages(bool automated, Action<Action> whenRibbonReady, Action stages)
        {
            if (automated)
            {
                Log.Info(SkipMessage);
                whenRibbonReady(() => Log.Info(RibbonReadyMessage));
                return false;
            }
            whenRibbonReady(stages);
            return true;
        }
    }
}
