namespace PyNavis.Runtime.Execution
{
    /// <summary>
    /// Lets one script run at a time.
    ///
    /// Needed because scripts now pump the message loop while they work (see
    /// UiPump): a pump dispatches queued input, so a second click on a ribbon
    /// button during a long run would start another script on top of the first.
    /// For the clash grouper that means a second run reading clash tests the
    /// first run is in the middle of replacing.
    ///
    /// Not re-entrant on purpose - a nested run is exactly what this exists to
    /// refuse. The console is unaffected: it shows non-modally, so RunScript has
    /// already returned and the gate is free by the time anything is typed.
    /// </summary>
    public static class RunGate
    {
        private static bool _held;

        /// <summary>What is running, for the message shown when a run is refused.</summary>
        public static string CurrentTitle { get; private set; }

        /// <summary>True and takes the gate when free; false when already held.</summary>
        public static bool TryEnter(string title)
        {
            if (_held) return false;
            _held = true;
            CurrentTitle = title;
            return true;
        }

        /// <summary>Releases the gate. Safe to call when it is not held.</summary>
        public static void Exit()
        {
            _held = false;
            CurrentTitle = null;
        }

        public static bool IsHeld => _held;
    }
}
