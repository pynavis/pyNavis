using System;
using System.Runtime.InteropServices;

namespace PyNavis.Runtime.Input
{
    /// <summary>
    /// Thread-scoped WH_KEYBOARD hook on the Navisworks UI thread. Sees keystrokes
    /// before the focused control regardless of native/WPF/WinForms focus; never
    /// global, never sees other applications. The callback returns true to swallow.
    /// </summary>
    internal static class KeyboardHook
    {
        private const int WH_KEYBOARD = 2;
        private const uint TransitionUp = 0x80000000;   // lParam bit 31: this is a keyup
        private const uint PreviousDown = 0x40000000;   // lParam bit 30: auto-repeat

        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint threadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        private static IntPtr _hook;
        private static HookProc _proc;                       // held so the GC cannot collect it
        private static Func<int, bool, bool, bool> _onKey;   // (vk, isKeyDown, isRepeat) -> swallow

        /// <summary>Installs on the CALLING thread; call from the UI thread only.</summary>
        public static bool Install(Func<int, bool, bool, bool> onKey)
        {
            if (_hook != IntPtr.Zero) return true;
            _onKey = onKey;
            _proc = Callback;
            _hook = SetWindowsHookEx(WH_KEYBOARD, _proc, IntPtr.Zero, GetCurrentThreadId());
            if (_hook == IntPtr.Zero)
                Log.Error($"Keyboard hook install failed (win32 error {Marshal.GetLastWin32Error()}) - chords disabled.");
            return _hook != IntPtr.Zero;
        }

        public static void Uninstall()
        {
            if (_hook == IntPtr.Zero) return;
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            _onKey = null;
        }

        private static IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && _onKey != null)
            {
                try
                {
                    var flags = unchecked((uint)lParam.ToInt64());
                    var isDown = (flags & TransitionUp) == 0;
                    var isRepeat = (flags & PreviousDown) != 0;
                    if (_onKey(wParam.ToInt32(), isDown, isRepeat))
                        return (IntPtr)1;   // handled: the app never sees the chord
                }
                catch (Exception ex)
                {
                    // A bug in dispatch must never eat the user's keystroke.
                    Log.Error("Keyboard hook callback failed", ex);
                }
            }
            return CallNextHookEx(_hook, code, wParam, lParam);
        }
    }
}
