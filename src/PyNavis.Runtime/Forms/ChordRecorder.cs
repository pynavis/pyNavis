using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PyNavis.Runtime.Input;
using Tokens = PyNavis.Runtime.Forms.DesignSystem.Tokens;

namespace PyNavis.Runtime.Forms
{
    /// <summary>
    /// Press-to-record chord field: focus it, press the keys, done. Every capture
    /// round-trips through ChordParser, so the recorder can never produce a
    /// binding the runtime would reject. Esc drops focus without changes,
    /// Backspace/Delete clears (= disabled). Keys are swallowed while focused so
    /// dialog buttons never react to a chord being recorded. Visuals follow
    /// design-system v2: hairline border, muted placeholder, accent only as the
    /// focus indication.
    /// </summary>
    public sealed class ChordRecorder : Border
    {
        private readonly TextBlock _label;
        private readonly Tokens _t;
        private readonly bool _allowBareKeys;
        private string _chordText;

        /// <summary>Raised on commit or clear; the payload is the new value (null = cleared).</summary>
        public event Action<string> ChordChanged;

        /// <summary>Last capture error ("bindings need Ctrl or Alt", unsupported key), or null.</summary>
        public string LastError { get; private set; }

        public ChordRecorder(bool allowBareKeys)
        {
            _allowBareKeys = allowBareKeys;
            _t = Tokens.Current;

            Focusable = true;
            Width = 150;
            Height = 26;
            CornerRadius = new CornerRadius(3);
            BorderThickness = new Thickness(1);
            BorderBrush = DesignSystem.Brush(_t.Line);
            Background = DesignSystem.Brush(_t.Paper);

            _label = DesignSystem.Text("", 12, _t.Muted);
            _label.VerticalAlignment = VerticalAlignment.Center;
            _label.HorizontalAlignment = HorizontalAlignment.Center;
            Child = _label;

            Refresh();

            MouseLeftButtonDown += (s, e) => { Focus(); e.Handled = true; };
            GotKeyboardFocus += (s, e) => BorderBrush = DesignSystem.Brush(DesignSystem.Accent);
            LostKeyboardFocus += (s, e) =>
            {
                BorderBrush = DesignSystem.Brush(_t.Line);
                LastError = null;
                Refresh();
            };
        }

        /// <summary>Canonical chord text, or null when unbound/disabled.</summary>
        public string ChordText
        {
            get => _chordText;
            set { _chordText = value; LastError = null; Refresh(); }
        }

        /// <summary>What the field currently shows (test seam).</summary>
        public string DisplayText => _label.Text;

        /// <summary>
        /// Whether recording swallows a key, given the RESOLVED key (Alt chords
        /// arrive as Key.System carrying the real key in SystemKey, so callers
        /// resolve first). Tab always passes through: a field that eats it is a
        /// keyboard trap with no way out, and that also lets Alt+Tab reach the
        /// OS while Alt+M is still recordable.
        /// </summary>
        public static bool HandlesKey(Key resolvedKey) => resolvedKey != Key.Tab;

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (!HandlesKey(key)) return;      // leave the field

            e.Handled = true;                  // otherwise nothing leaks to the dialog

            if (key == Key.Escape)
            {
                // hand focus back rather than dropping it into nowhere
                MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous));
                return;
            }
            if (key == Key.Back || key == Key.Delete)
            {
                _chordText = null;
                LastError = null;
                Refresh();
                ChordChanged?.Invoke(null);
                return;
            }

            var chord = ChordFromKey(key, Keyboard.Modifiers, _allowBareKeys, out var error);
            LastError = error;
            if (chord != null)
            {
                _chordText = chord;
                Refresh();
                ChordChanged?.Invoke(chord);
            }
            else if (error != null)
            {
                Refresh();
            }
        }

        /// <summary>
        /// Pure mapping from a pressed key to canonical chord text. Returns null with
        /// null error while only modifiers are down, null with an error for a key the
        /// parser cannot express or a policy violation (bare key without opt-in).
        /// </summary>
        public static string ChordFromKey(
            Key key, ModifierKeys modifiers, bool allowBareKeys, out string error)
        {
            error = null;
            switch (key)
            {
                case Key.LeftCtrl:
                case Key.RightCtrl:
                case Key.LeftShift:
                case Key.RightShift:
                case Key.LeftAlt:
                case Key.RightAlt:
                case Key.LWin:
                case Key.RWin:
                    return null; // still recording
            }

            var chord = new Chord
            {
                Ctrl = (modifiers & ModifierKeys.Control) != 0,
                Alt = (modifiers & ModifierKeys.Alt) != 0,
                Shift = (modifiers & ModifierKeys.Shift) != 0,
                VirtualKey = KeyInterop.VirtualKeyFromKey(key),
            };

            // Round-trip through the parser: NameOf yields "0xNN" for keys the
            // binding grammar has no name for, which TryParse then rejects.
            var text = chord.ToString();
            if (!ChordParser.TryParse(text, allowBareKeys, out _, out error))
                return null;
            return text;
        }

        private void Refresh()
        {
            if (LastError != null)
            {
                _label.Text = LastError.Contains("Ctrl or Alt") ? "needs Ctrl or Alt" : "unsupported key";
                _label.Foreground = DesignSystem.Brush(_t.Muted);
            }
            else if (_chordText == null)
            {
                _label.Text = "press keys";
                _label.Foreground = DesignSystem.Brush(_t.Muted);
            }
            else
            {
                _label.Text = _chordText;
                _label.Foreground = DesignSystem.Brush(_t.Ink);
            }
        }
    }
}
