namespace PyNavis.Runtime.Output
{
    /// <summary>
    /// What a script-output surface can do. HtmlOutputWindow renders it all; the
    /// classic text OutputWindow degrades rich calls to plain text so scripts never
    /// fail just because the WebView2 runtime is missing on a machine.
    /// </summary>
    public interface IOutputWindow
    {
        void AppendText(string text, bool isError);
        void AppendHtml(string html);
        void ShowProgress(double fraction, string label);
        /// <summary>HTML (or plain text on degrade) for a clickable element link.</summary>
        string RegisterElementLink(object item, string label);
        void Show();
        void SetTitle(string title);

        /// <summary>
        /// Raised when the window closes. Both implementations derive from Window,
        /// whose own Closed event satisfies this implicitly. The writer listens so
        /// it can forget a closed window: without it, every later write goes to a
        /// dead window and the script's output vanishes with no error anywhere.
        /// </summary>
        event System.EventHandler Closed;

        /// <summary>
        /// The window's dispatcher, so callers can pump it (see UiPump). Both
        /// implementations derive from Window, whose DispatcherObject.Dispatcher
        /// satisfies this implicitly and is non-null from construction - which is
        /// why this is preferred over Dispatcher.CurrentDispatcher, which would
        /// silently manufacture a dispatcher if ever touched off-thread.
        /// </summary>
        System.Windows.Threading.Dispatcher Dispatcher { get; }
    }
}
