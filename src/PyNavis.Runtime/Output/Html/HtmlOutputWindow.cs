using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using PyNavis.Runtime.Forms;

namespace PyNavis.Runtime.Output.Html
{
    /// <summary>
    /// The rich output window: a WebView2 browser rendering the
    /// HtmlPage skeleton. Appends queue until the browser is ready, then stream as
    /// ExecuteScriptAsync calls. Element-link clicks come back over WebMessage and
    /// select the ModelItem in the running Navisworks document.
    /// </summary>
    public class HtmlOutputWindow : Window, IOutputWindow
    {
        private readonly WebView2 _view;
        private readonly ElementRegistry _elements = new ElementRegistry();
        private readonly List<string> _pending = new List<string>();
        // The same output as _pending, kept as readable text for as long as the browser
        // is not up, so a browser that never comes up cannot take the output with it.
        private readonly System.Text.StringBuilder _pendingText = new System.Text.StringBuilder();
        private System.Windows.Controls.TextBox _fallback;
        private bool _ready;
        private bool _closed;

        public HtmlOutputWindow(string title)
        {
            Title = title;
            Width = 820;
            Height = 560;
            MinWidth = 460;
            MinHeight = 320;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var palette = OutputPalette.For(PyNavisTheme.IsDark);
            Background = new SolidColorBrush(
                Color.FromRgb((byte)palette.BackgroundR, (byte)palette.BackgroundG, (byte)palette.BackgroundB));
            // Design system v2: the real Windows titlebar (no custom gradient bar),
            // a solid opaque page (no acrylic). Content styling lives in HtmlPage.
            FluentChrome.Apply(this);

            _view = new WebView2
            {
                DefaultBackgroundColor = System.Drawing.Color.FromArgb(
                    palette.BackgroundR, palette.BackgroundG, palette.BackgroundB),
            };
            Content = _view;

            Loaded += async (s, e) => await InitAsync(palette);
            // Scripts pump the message loop while they work (see UiPump), so the
            // user can now close this window mid-run - which was impossible when
            // the thread never yielded. A later ExecuteScriptAsync on the
            // disposed CoreWebView2 would throw, unwind through the script's
            // progress callback, abort the run, and then throw a SECOND time out
            // of ScriptExecutor's catch when it tried to report the failure into
            // this same dead window. So: stop talking to it once it is gone.
            Closed += (s, e) =>
            {
                _closed = true;
                _pending.Clear();
            };
        }

        private async System.Threading.Tasks.Task InitAsync(OutputPalette palette)
        {
            if (_ready) return;
            try
            {
                WebView2Probe.PreloadNativeLoader();
                var dataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "pyNavis", "webview2");
                var env = await CoreWebView2Environment.CreateAsync(null, dataDir);
                await _view.EnsureCoreWebView2Async(env);

                _view.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                _view.CoreWebView2.WebMessageReceived += OnWebMessage;
                // A link a script prints belongs in the user's browser, not in a bare
                // WebView2 popup inside Navisworks (target=_blank) or in place of the
                // output itself (a plain click).
                _view.CoreWebView2.NewWindowRequested += (s, e) =>
                {
                    e.Handled = true;
                    OpenExternally(e.Uri);
                };
                _view.CoreWebView2.NavigationStarting += (s, e) =>
                {
                    if (!IsExternalLink(e.Uri)) return;
                    e.Cancel = true;
                    OpenExternally(e.Uri);
                };

                var accent = FluentChrome.AccentColor();
                _view.NavigateToString(HtmlPage.Skeleton(
                    palette, $"#{accent.R:X2}{accent.G:X2}{accent.B:X2}"));
                _view.CoreWebView2.NavigationCompleted += (s, e) =>
                {
                    _ready = true;
                    _pendingText.Clear();
                    foreach (var script in _pending)
                        _view.CoreWebView2.ExecuteScriptAsync(script);
                    _pending.Clear();
                };
            }
            catch (Exception ex)
            {
                FallBackToText(ex);
            }
        }

        /// <summary>
        /// The WebView2 runtime can be installed and still fail to start (blocked
        /// user-data folder, roaming profile, locked-down machine). Logging that and
        /// staying "not ready" left every printed line queued behind a blank window.
        /// Swap the browser for a plain read-only text box, show what was queued, and
        /// keep appending there: rich output degrades exactly like the classic window.
        /// </summary>
        internal void FallBackToText(Exception reason)
        {
            if (_fallback != null) return;
            Log.Error("WebView2 output failed to initialize - showing plain text instead.", reason);

            _fallback = new System.Windows.Controls.TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(12),
                FontFamily = new FontFamily("Consolas"),
                Background = Background,
                Foreground = new SolidColorBrush(PyNavisTheme.IsDark ? Colors.White : Colors.Black),
                Text = _pendingText.ToString(),
            };
            _pending.Clear();
            _pendingText.Clear();
            Content = _fallback;
        }

        /// <summary>What the plain-text fallback shows; null while the browser path is in use.</summary>
        internal string FallbackText => _fallback?.Text;

        /// <summary>Text for the fallback box; true when it took the output.</summary>
        private bool TryFallback(string text)
        {
            if (_closed) return true;
            if (!Dispatcher.CheckAccess()) return Dispatcher.Invoke(() => TryFallback(text));
            if (_fallback != null)
            {
                _fallback.AppendText(text);
                _fallback.ScrollToEnd();
                return true;
            }
            if (!_ready) _pendingText.Append(text);
            return false;
        }

        /// <summary>Whether a link leaves the output window for the user's default
        /// handler. Web and mail only: never file: or javascript:, which a script's
        /// printed HTML must not be able to launch.</summary>
        internal static bool IsExternalLink(string uri)
        {
            if (string.IsNullOrEmpty(uri)) return false;
            return uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || uri.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);
        }

        private static void OpenExternally(string uri)
        {
            if (!IsExternalLink(uri)) return;
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error($"Could not open link '{uri}'.", ex);
            }
        }

        private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                // payload: {"select": <index>} - avoid a JSON dependency for one int
                var json = e.WebMessageAsJson;
                var match = System.Text.RegularExpressions.Regex.Match(json, "\"select\"\\s*:\\s*(\\d+)");
                if (!match.Success) return;
                if (!_elements.TryResolve(int.Parse(match.Groups[1].Value), out var item)) return;
                SelectInModel(item);
            }
            catch (Exception ex)
            {
                Log.Error("Element link click failed.", ex);
            }
        }

        private static void SelectInModel(object item)
        {
            var modelItem = item as Autodesk.Navisworks.Api.ModelItem;
            if (modelItem == null) return;
            var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            if (doc == null) return;
            var collection = new Autodesk.Navisworks.Api.ModelItemCollection { modelItem };
            doc.CurrentSelection.CopyFrom(collection);
        }

        private void Run(string script)
        {
            if (_closed || _fallback != null) return;
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => Run(script));
                return;
            }
            if (!_ready)
            {
                _pending.Add(script);
                return;
            }
            try
            {
                _view.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                // Belt and braces behind the _closed check: the WebView2 can also
                // die on its own (browser process crash). Output must never be
                // able to fail a script.
                _closed = true;
                Log.Error("Output window is no longer usable; dropping output.", ex);
            }
        }

        // ---- IOutputWindow ----------------------------------------------------

        public void AppendText(string text, bool isError)
        {
            if (!TryFallback(text)) Run(HtmlPage.AppendScript(isError ? "error" : "text", text));
        }

        public void AppendHtml(string html)
        {
            if (!TryFallback(html + Environment.NewLine)) Run(HtmlPage.AppendScript("html", html));
        }

        public void ShowProgress(double fraction, string label) =>
            Run(HtmlPage.ProgressScript(fraction, label));

        public string RegisterElementLink(object item, string label) =>
            HtmlPage.ElementLinkHtml(_elements.Register(item), label);

        public void SetTitle(string title)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetTitle(title));
                return;
            }
            Title = title;
        }
    }

    /// <summary>One-time probe for the Evergreen WebView2 runtime.</summary>
    public static class WebView2Probe
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string path);

        private static bool? _available;
        private static bool _nativeLoaderLoaded;

        public static bool IsAvailable
        {
            get
            {
                if (_available == null)
                {
                    try
                    {
                        PreloadNativeLoader();
                        _available = !string.IsNullOrEmpty(
                            CoreWebView2Environment.GetAvailableBrowserVersionString());
                    }
                    catch (Exception ex)
                    {
                        Log.Info("WebView2 runtime not available - using text output. " + ex.Message);
                        _available = false;
                    }
                }
                return _available.Value;
            }
        }

        /// <summary>
        /// WebView2's managed dll P/Invokes WebView2Loader.dll, which Windows would
        /// search for next to Roamer.exe. Load it once from our runtime dir instead.
        /// </summary>
        internal static void PreloadNativeLoader()
        {
            if (_nativeLoaderLoaded) return;
            var runtimeDir = Path.GetDirectoryName(typeof(WebView2Probe).Assembly.Location);
            var native = Path.Combine(runtimeDir, "runtimes", "win-x64", "native", "WebView2Loader.dll");
            if (File.Exists(native) && LoadLibrary(native) != IntPtr.Zero)
                _nativeLoaderLoaded = true;
            else
                Log.Error($"WebView2Loader.dll preload failed from '{native}' (ok outside a deployed runtime).");
        }
    }
}
