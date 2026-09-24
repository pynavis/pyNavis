namespace PyNavis.Runtime.Ribbon
{
    /// <summary>Decisions a dockpane's ribbon toggle makes, kept pure so they are testable
    /// without an AdWindows ribbon.</summary>
    public static class DockPaneButtons
    {
        /// <summary>Icon path for the toggle: the on art while the pane is open, when the
        /// bundle ships one.</summary>
        public static string IconFor(string offIcon, string onIcon, bool pressed) =>
            pressed && !string.IsNullOrEmpty(onIcon) ? onIcon : offIcon;

        /// <summary>Ribbon click routing for a dockpane, through the same
        /// ClickActions contract as every other button (Alt wins when other
        /// modifiers are held). Shift would mean config.py on a pushbutton;
        /// a dockpane has none, so Config falls through to the toggle.</summary>
        public static bool OpensFolder(System.Windows.Input.ModifierKeys modifiers) =>
            Execution.ClickActions.For(modifiers) == Execution.ClickAction.OpenFolder;

        /// <summary>
        /// What to toast when a dockpane's ribbon toggle could not show its pane. Two
        /// distinct causes look identical to the click (SetVisible returns false either
        /// way), but they are not the same problem: genuine overflow needs more slots,
        /// while a slot claimed this session by Panel slots but not yet a registered
        /// plugin type just needs the restart it already told the user about.
        /// </summary>
        public static (string headline, string detail) OverflowMessage(string title, bool pendingRestart)
        {
            if (pendingRestart)
                return ("Panel slot ready after restart",
                    "This panel's new slot was just generated. It registers the next time " +
                    "Navisworks restarts, not after Reload.");
            return ("No panel slot free for " + title,
                "Every pyNavis panel slot is claimed. Add more with Panel slots on the pyNavis tab, then restart Navisworks.");
        }
    }
}
