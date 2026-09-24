using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using PyNavis.Runtime.Forms;
using WinForms = System.Windows.Forms;

namespace PyNavis.Runtime.Panes
{
    /// <summary>
    /// The WinForms half of a dock pane: a UserControl holding one ElementHost, which
    /// holds whatever WPF content the claiming bundle produced.
    ///
    /// Field-measured: Navisworks parents the control returned from
    /// CreateControlPane but never runs a WinForms layout pass over it, so Dock and
    /// Anchor on the pane's top-level control are inert and it sits at the UserControl
    /// default 150x150 forever. Following the parent's client rectangle by hand is what
    /// actually fills the pane.
    /// </summary>
    public class PaneShell : WinForms.UserControl
    {
        private readonly ElementHost _host;
        private readonly Border _root;
        private readonly ScrollViewer _scroller;
        private WinForms.Control _watched;

        public int Slot { get; }

        /// <summary>The WPF element currently displayed; null before the first SetContent.</summary>
        public UIElement CurrentContent { get; private set; }

        public PaneShell(int slot)
        {
            Slot = slot;
            var tokens = DesignSystem.Tokens.Current;

            // Scrolls rather than clips: a docked pane can be dragged narrower than its
            // content, and clipped content reads as broken content.
            _scroller = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            _root = new Border
            {
                Background = DesignSystem.Brush(tokens.Paper),
                Child = _scroller,
            };
            _host = new ElementHost
            {
                Dock = WinForms.DockStyle.Fill,
                Child = _root,
                BackColor = System.Drawing.Color.FromArgb(tokens.Paper.R, tokens.Paper.G, tokens.Paper.B),
            };
            Controls.Add(_host);
            Dock = WinForms.DockStyle.Fill;   // honoured if the host ever does lay us out
        }

        /// <summary>Virtual only so a test can hook it to simulate a reentrant call into
        /// PaneRegistry.Fill without a live Navisworks host; production code never
        /// overrides it.</summary>
        public virtual void SetContent(UIElement content)
        {
            CurrentContent = content;
            _scroller.Content = content;
        }

        /// <summary>
        /// Renames the pane's title bar from <paramref name="stale"/> - the slot plugin's
        /// DisplayName, "pyNavis Panel N" - to the bundle's own title. Returns false when
        /// the caption-bearing ancestor is not there to write to.
        ///
        /// A pane's caption comes from its plugin's DisplayName, which is a compile-time
        /// constant on the slot and read-only on PluginRecord. Field-measured,
        /// the caption is really just Text on an ancestor control, four up from the
        /// ElementHost: PaneShell > Panel > NWDockWindow > Syncfusion DockHost. Matching on
        /// the caption rather than on that type keeps the walk independent of Navisworks'
        /// internal docking library, and makes a second call a no-op. The caller passes the
        /// caption in, read back from the plugin record, so no copy of that string is kept
        /// here to drift from the loader's attributes or the satellite's generated source.
        ///
        /// Timing matters and is why this is not called from Fill: at content-build time
        /// the shell is still parked in the WindowsFormsParkingWindow with no such ancestor
        /// (measured: the chain stops at Panel). The host adopts it into the docking frame
        /// shortly afterwards, before OnVisibleChanged - so the visibility hook is the
        /// earliest reliable caller.
        ///
        /// Only the title bar changes. Navisworks' View > Windows menu reads Syncfusion's
        /// own label store, which is not reachable from the host control, so that entry
        /// keeps saying "pyNavis Panel N".
        /// </summary>
        public bool Retitle(string stale, string title)
        {
            if (string.IsNullOrEmpty(stale) || string.IsNullOrEmpty(title) || title == stale)
                return false;
            if (!Swap(stale, title)) return false;

            _hostCaption = stale;
            _ourCaption = title;
            return true;
        }

        /// <summary>What the host called this slot, and what we replaced it with.</summary>
        private string _hostCaption, _ourCaption;

        /// <summary>
        /// Puts the host's "pyNavis Panel N" caption back, for a slot whose bundle has gone
        /// away. Without this the retitle is one-way: a slot that loses its claim keeps
        /// reading as the removed bundle while its body says the slot is not in use, and a
        /// different bundle claiming the slot later would inherit that stale name until it
        /// retitles. Uses the caption this shell actually overwrote rather than rebuilding
        /// it, so this path needs no plugin-record lookup.
        /// </summary>
        public bool RestoreCaption()
        {
            if (_ourCaption == null) return false;
            if (!Swap(_ourCaption, _hostCaption)) return false;

            _hostCaption = _ourCaption = null;
            return true;
        }

        /// <summary>Renames the nearest ancestor currently captioned <paramref name="from"/>.</summary>
        private bool Swap(string from, string to)
        {
            var ancestor = Parent;
            for (var depth = 0; ancestor != null && depth < 12; depth++)
            {
                if (ancestor.Text == from)
                {
                    ancestor.Text = to;
                    return true;
                }
                ancestor = ancestor.Parent;
            }
            return false;
        }

        /// <summary>Themed placeholder, for a slot that is unclaimed, still loading, or broken.</summary>
        public void ShowMessage(string headline, string detail)
        {
            var tokens = DesignSystem.Tokens.Current;
            var body = new StackPanel { Margin = new Thickness(16) };
            body.Children.Add(DesignSystem.Text(headline, 14, tokens.Ink, FontWeights.SemiBold));
            if (!string.IsNullOrEmpty(detail))
                body.Children.Add(DesignSystem.Text(detail, 11.5, tokens.Muted));
            SetContent(body);
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);

            if (_watched != null)
            {
                _watched.Resize -= OnParentResize;
                _watched = null;
            }
            var parent = Parent;
            if (parent == null) return;

            parent.Resize += OnParentResize;
            _watched = parent;
            FillParent();
        }

        private void OnParentResize(object sender, EventArgs e) => FillParent();

        private void FillParent()
        {
            var parent = Parent;
            if (parent == null) return;
            var area = parent.ClientRectangle;
            if (area.Width <= 0 || area.Height <= 0) return;
            if (Bounds != area) Bounds = area;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _watched != null)
            {
                _watched.Resize -= OnParentResize;
                _watched = null;
            }
            base.Dispose(disposing);
        }
    }
}
