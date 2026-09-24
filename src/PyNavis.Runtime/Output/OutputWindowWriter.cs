using System.Text;
using PyNavis.Runtime.Output.Html;

namespace PyNavis.Runtime.Output
{
    /// <summary>
    /// TextWriter that materializes an output window on first write (behavior:
    /// scripts that print nothing show no window). Creates the WebView2 HTML window
    /// when the runtime is available, the classic text window otherwise. Also the
    /// door for rich output: WriteHtml / Progress / ElementLink land in the same
    /// window, in call order with plain prints.
    /// </summary>
    public class OutputWindowWriter : System.IO.TextWriter
    {
        private readonly OutputTranscript _transcript = new OutputTranscript();
        private IOutputWindow _window;
        private string _currentTitle;
        private int _lastPump;

        public OutputWindowWriter(string title)
        {
            _currentTitle = title;
            // Never 0 - that is the sentinel that breaks Due() past 24.9 days of
            // uptime. Seeding minus the interval also makes the first Progress
            // pump immediately, which is what gets the window on screen early.
            _lastPump = UiPump.Seed();
        }

        public override Encoding Encoding => Encoding.UTF8;

        private IOutputWindow Window
        {
            get
            {
                if (_window == null)
                {
                    // _currentTitle, not the constructor's: a SetTitle call before
                    // the first print is a real retitle, not a no-op, and it has to
                    // survive until there is a window to carry it.
                    var window = WebView2Probe.IsAvailable
                        ? (IOutputWindow)new HtmlOutputWindow(_currentTitle)
                        : new OutputWindow(_currentTitle);
                    // Closing the window must not silently mute the script. Forget
                    // it here and the next write opens a fresh one under the same
                    // title; nothing else in the chain notices a window has gone,
                    // so without this every later write lands in a dead window.
                    window.Closed += (s, e) => { if (_window == window) _window = null; };
                    window.Show();
                    _window = window;
                }
                return _window;
            }
        }

        /// <summary>True once the live window has actually materialized. Lets
        /// callers decide to stay silent rather than POP a window (reading
        /// <see cref="Window"/> creates one) - see PyNavisHost.HasOpenOutput.</summary>
        public bool HasOpenWindow => _window != null;

        /// <summary>The title the window carries, or will carry when it opens.</summary>
        public string EffectiveTitle => _currentTitle;

        public override void Write(char value) => Write(value.ToString());

        // TextWriterStream (IronPython stdout) writes through this overload, not
        // Write(string) or Write(char). Without it, the un-overridden net48
        // TextWriter base loops Write(char) once per character, turning every
        // bulk print() into per-character fragments (and, on the live WebView2
        // path, one ExecuteScriptAsync call per character).
        public override void Write(char[] buffer, int index, int count) =>
            Write(new string(buffer, index, count));

        public override void Write(string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            _transcript.Record("text", value);
            Window.AppendText(value, false);
        }

        /// <summary>Appends error-colored text (tracebacks).</summary>
        public void WriteError(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _transcript.Record("error", text);
            Window.AppendText(text, true);
        }

        /// <summary>Appends an HTML fragment (plain text window shows the source).</summary>
        public void WriteHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) return;
            _transcript.Record("html", html);
            Window.AppendHtml(html);
        }

        /// <summary>
        /// A standalone HTML page replaying every fragment written so far, in
        /// order - independent of whether a live window ever opened.
        /// </summary>
        public string Transcript => _transcript.Transcript;

        /// <summary>Writes the transcript (see <see cref="Transcript"/>) to disk as UTF-8.</summary>
        public void SaveTranscript(string path) =>
            System.IO.File.WriteAllText(path, Transcript, Encoding.UTF8);

        /// <summary>Retitles the output window. Before the window materializes
        /// the title is held and applied when it opens, so output.set_title()
        /// called ahead of the first print is not silently lost.</summary>
        public void SetTitle(string title)
        {
            _currentTitle = title;
            if (_window != null) _window.SetTitle(title);
        }

        /// <summary>Shows/updates the progress bar; fraction 0..1, 1.0 completes it.
        ///
        /// Also pumps the message loop, throttled. Without that the bar is
        /// invisible: a script holds the UI thread for its whole run, so the
        /// queued paint never happens. Worse, HtmlOutputWindow's Loaded event
        /// never fires either, so InitAsync never runs and EVERY script queues
        /// unsent in _pending - the WebView2 does not merely fail to repaint, it
        /// never initialises until the script ends. The first pump here is what
        /// lets it come up mid-run.
        ///
        /// Progress only, not text writes: reporting progress is an explicit "I
        /// am going to be a while" signal, whereas pumping inside a tight print
        /// loop would hand arbitrary re-entrancy to scripts that never asked for
        /// it.</summary>
        public void Progress(double fraction, string label)
        {
            var window = Window;
            window.ShowProgress(fraction, label);
            var now = System.Environment.TickCount;
            if (!UiPump.Due(now, _lastPump)) return;
            _lastPump = now;
            UiPump.Pump(window.Dispatcher);
        }

        /// <summary>HTML for a clickable link that selects the item in the model.</summary>
        public string ElementLink(object item, string label) =>
            Window.RegisterElementLink(item, label);
    }
}
