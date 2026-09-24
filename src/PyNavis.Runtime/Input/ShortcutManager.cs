using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using PyNavis.Runtime.Bundles;
using PyNavis.Runtime.Config;
using PyNavis.Runtime.Execution;

namespace PyNavis.Runtime.Input
{
    /// <summary>
    /// Glues the pure ShortcutMap to the keyboard hook: configured from parsed bundles
    /// plus user config before each ribbon build, dispatches matched chords through
    /// ScriptExecutor exactly as a button click would. A chord suppressed by any guard
    /// falls through to the application untouched; only a dispatched chord is swallowed.
    /// </summary>
    public static class ShortcutManager
    {
        private static ShortcutMap _map = ShortcutMap.Build(
            new PushButtonModel[0], new Dictionary<string, string>(), false);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int vk);

        private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;

        /// <summary>Rebuilds the chord table from freshly parsed bundles and config.</summary>
        public static void Configure(IReadOnlyList<ExtensionModel> extensions, PyNavisConfig config)
        {
            var buttons = extensions
                .SelectMany(e => e.Tabs).SelectMany(t => t.Panels).SelectMany(p => p.Buttons)
                .ToList();
            _map = ShortcutMap.Build(buttons, config.ShortcutBindings, config.ShortcutsAllowBareKeys);
            foreach (var problem in _map.Problems)
                Log.Error("Shortcut: " + problem);
            Log.Info($"Shortcuts: {_map.Count} binding(s) active.");
        }

        /// <summary>Resolved binding for the tooltip suffix, or null.</summary>
        public static Chord? BindingFor(PushButtonModel button) => _map.BindingFor(button);

        public static void InstallHook() => KeyboardHook.Install(OnKey);

        public static void UninstallHook() => KeyboardHook.Uninstall();

        /// <summary>The guard rules, pure for testing: dispatch only on a fresh keydown,
        /// with the main window foreground and focus outside any text input.</summary>
        public static bool ShouldDispatch(
            bool isKeyDown, bool isRepeat, bool foregroundIsMain, bool focusIsTextInput) =>
            isKeyDown && !isRepeat && foregroundIsMain && !focusIsTextInput;

        private static bool OnKey(int virtualKey, bool isDown, bool isRepeat)
        {
            if (_map.Count == 0) return false;

            var chord = new Chord
            {
                Ctrl = (GetKeyState(VK_CONTROL) & 0x8000) != 0,
                Alt = (GetKeyState(VK_MENU) & 0x8000) != 0,
                Shift = (GetKeyState(VK_SHIFT) & 0x8000) != 0,
                VirtualKey = virtualKey,
            };
            if (!_map.TryGetButton(chord, out var button)) return false;
            if (!ShouldDispatch(isDown, isRepeat, ForegroundIsMainWindow(), FocusIsTextInput()))
                return false;

            // Run after the hook chain returns, exactly as a click would.
            Dispatcher.CurrentDispatcher.BeginInvoke(
                new Action(() => ScriptExecutor.Run(button)));
            return true;
        }

        private static bool ForegroundIsMainWindow()
        {
            try
            {
                var main = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                return main != IntPtr.Zero && GetForegroundWindow() == main;
            }
            catch { return false; }
        }

        private static bool FocusIsTextInput()
        {
            try
            {
                var focused = Keyboard.FocusedElement;
                if (focused is TextBoxBase || focused is System.Windows.Controls.PasswordBox)
                    return true;

                var hwnd = GetFocus();
                if (hwnd == IntPtr.Zero) return false;
                var name = new StringBuilder(64);
                GetClassName(hwnd, name, name.Capacity);
                return name.ToString().IndexOf("edit", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return true; }   // cannot tell -> assume typing, stay safe
        }
    }
}
