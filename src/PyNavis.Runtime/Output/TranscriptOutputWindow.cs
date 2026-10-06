using System;
using System.IO;
using System.Windows.Threading;

namespace PyNavis.Runtime.Output
{
    /// <summary>
    /// The output "window" of a <c>pynavis run</c>: no window at all, just the run's
    /// transcript. Text goes in as written (errors prefixed so they stand out in a plain
    /// file), HTML as its source, and what only makes sense on screen (progress, titles,
    /// element links) degrades to nothing or to its label.
    /// </summary>
    public sealed class TranscriptOutputWindow : IOutputWindow
    {
        private readonly TextWriter _writer;

        public TranscriptOutputWindow(TextWriter writer)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        public void AppendText(string text, bool isError) =>
            _writer.Write(isError ? "ERROR: " + text : text);

        public void AppendHtml(string html) => _writer.Write(html);

        public void ShowProgress(double fraction, string label) { }

        /// <summary>No model to select from a text file, so the link is just its label.</summary>
        public string RegisterElementLink(object item, string label) => label;

        public void Show() { }

        public void SetTitle(string title) { }

        /// <summary>Never raised: nothing can close a transcript under the script.</summary>
        public event EventHandler Closed { add { } remove { } }

        /// <summary>The calling thread's dispatcher, which is the runner's own thread.</summary>
        public Dispatcher Dispatcher => Dispatcher.CurrentDispatcher;
    }
}
