using System;
using System.Windows.Input;

namespace PyNavis.Runtime.Execution
{
    public enum ClickAction { Primary, Config, OpenFolder }

    /// <summary>
    /// Modifier clicks: Alt+Click opens the bundle folder with the
    /// script selected (Alt wins when both are held), Shift+Click runs the bundle's
    /// config.py. Ctrl is deliberately ignored so keyboard chords - which arrive
    /// with their own modifiers still down - and plain clicks stay identical.
    /// Only ribbon clicks route through here; shortcut dispatch calls Run directly.
    /// </summary>
    public static class ClickActions
    {
        public static ClickAction For(ModifierKeys modifiers)
        {
            if ((modifiers & ModifierKeys.Alt) != 0) return ClickAction.OpenFolder;
            if ((modifiers & ModifierKeys.Shift) != 0) return ClickAction.Config;
            return ClickAction.Primary;
        }

        public static string ExplorerArgs(string scriptPath) => $"/select,\"{scriptPath}\"";

        public static void OpenFolder(string scriptPath)
        {
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", ExplorerArgs(scriptPath));
            }
            catch (Exception ex)
            {
                Log.Error($"Could not open folder for '{scriptPath}'", ex);
            }
        }
    }
}
