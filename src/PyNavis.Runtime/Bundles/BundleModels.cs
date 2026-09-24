using System.Collections.Generic;
using System.Linq;

namespace PyNavis.Runtime.Bundles
{
    /// <summary>An <c>*.extension</c> folder: a tree of tabs, panels, and pushbuttons.</summary>
    public class ExtensionModel
    {
        public string Name { get; set; }
        public string Directory { get; set; }
        /// <summary>Optional <c>lib\</c> folder shared by all scripts in the extension; null if absent.</summary>
        public string LibDirectory { get; set; }
        /// <summary>startup.py at the extension root; runs at boot and reload before the ribbon builds. Null if absent.</summary>
        public string StartupScriptPath { get; set; }
        public string DefaultEngineId { get; set; } = "ironpython";
        public List<TabModel> Tabs { get; } = new List<TabModel>();
        public List<HookModel> Hooks { get; } = new List<HookModel>();
        /// <summary>Folders the parser skipped and why, one line each (also logged). A
        /// skipped bundle is otherwise invisible: the author just sees no button.</summary>
        public List<string> Problems { get; } = new List<string>();
    }

    public class TabModel
    {
        public string Title { get; set; }
        /// <summary>Stable ribbon id, e.g. "PYNAVIS_TAB_pyNavis".</summary>
        public string Id { get; set; }
        public List<PanelModel> Panels { get; } = new List<PanelModel>();
    }

    /// <summary>Anything that can sit directly on a panel: a large button, a stack, a pulldown.</summary>
    public abstract class PanelItem
    {
    }

    /// <summary>A panel item that renders a caption: everything except a stack.
    ///
    /// A long caption stretches its panel wide, so bundle.yaml may break one with
    /// a literal <c>\n</c> (<c>title: Export\nViewpoints</c>). Only the ribbon
    /// caption wraps: <see cref="Title"/> stays the flat one-line form that logs,
    /// toasts, the output window title, keytips and <c>__title__</c> all use.
    /// </summary>
    public abstract class CaptionedPanelItem : PanelItem
    {
        private string _title;

        /// <summary>The caption on one line, breaks flattened to spaces.</summary>
        public string Title
        {
            get => _title;
            set
            {
                _title = BundleTitles.Flatten(value);
                RibbonTitle = BundleTitles.ForRibbon(value);
            }
        }

        /// <summary>The caption as the ribbon draws it, breaks expanded to newlines.</summary>
        public string RibbonTitle { get; private set; }
    }

    public class PanelModel
    {
        public string Title { get; set; }

        /// <summary>Panel contents in folder order (buttons, stacks, pulldowns interleaved).</summary>
        public List<PanelItem> Items { get; } = new List<PanelItem>();

        /// <summary>Contents of the panel's *.slideout folder(s), in folder order: shown
        /// below a panel break, which the host reveals when the panel title is clicked.</summary>
        public List<PanelItem> Slideout { get; } = new List<PanelItem>();

        /// <summary>Every leaf pushbutton on the panel, in order, whatever it sits inside.</summary>
        public IReadOnlyList<PushButtonModel> Buttons
        {
            get
            {
                var all = new List<PushButtonModel>();
                foreach (var item in Items.Concat(Slideout))
                {
                    if (item is PushButtonModel button) all.Add(button);
                    else if (item is StackModel stack) all.AddRange(stack.Buttons);
                    else if (item is PulldownModel pulldown) all.AddRange(pulldown.Buttons);
                }
                return all;
            }
        }
    }

    /// <summary>Two or three small buttons stacked in one panel column.</summary>
    public class StackModel : PanelItem
    {
        public string Directory { get; set; }
        public List<PushButtonModel> Buttons { get; } = new List<PushButtonModel>();
    }

    /// <summary>How a pulldown-shaped bundle renders its header.</summary>
    public enum PulldownKind
    {
        /// <summary>*.pulldown: header only opens the menu.</summary>
        Menu,
        /// <summary>*.splitbutton: header runs the last-used child.</summary>
        SplitLastUsed,
        /// <summary>*.splitpushbutton: header always runs the first child.</summary>
        SplitFixed,
    }

    /// <summary>A menu button holding any number of pushbuttons.</summary>
    public class PulldownModel : CaptionedPanelItem
    {
        public PulldownKind Kind { get; set; } = PulldownKind.Menu;
        public string Tooltip { get; set; }
        public string IconPath { get; set; }
        /// <summary>icon.dark.png; falls back to <see cref="IconPath"/> when absent.</summary>
        public string DarkIconPath { get; set; }
        /// <summary>icon.small.png, the simplified small-density glyph; falls back to <see cref="IconPath"/>.</summary>
        public string SmallIconPath { get; set; }
        /// <summary>icon.small.dark.png; falls back to <see cref="IconPath"/>.</summary>
        public string SmallDarkIconPath { get; set; }
        /// <summary>Explicit Alt-keytip from bundle.yaml "keytip:"; null = auto-assign.</summary>
        public string KeyTipOverride { get; set; }
        public string Directory { get; set; }
        public List<PushButtonModel> Buttons { get; } = new List<PushButtonModel>();
    }

    public class PushButtonModel : CaptionedPanelItem
    {
        public string Tooltip { get; set; }
        public string ScriptPath { get; set; }
        /// <summary>config.py beside script.py (the Shift+Click secondary action); null if absent.</summary>
        public string ConfigScriptPath { get; set; }
        public string Directory { get; set; }
        /// <summary>icon.png in the bundle folder; null if absent.</summary>
        public string IconPath { get; set; }
        /// <summary>icon.dark.png; falls back to <see cref="IconPath"/> when absent.</summary>
        public string DarkIconPath { get; set; }
        /// <summary>icon.small.png, the simplified small-density glyph; falls back to <see cref="IconPath"/>.</summary>
        public string SmallIconPath { get; set; }
        /// <summary>icon.small.dark.png; falls back to <see cref="IconPath"/>.</summary>
        public string SmallDarkIconPath { get; set; }
        /// <summary>Author-default chord from bundle.yaml "shortcut:"; raw string, validated later.</summary>
        public string Shortcut { get; set; }
        /// <summary>Explicit Alt-keytip from bundle.yaml "keytip:"; null = auto-assign.</summary>
        public string KeyTipOverride { get; set; }
        /// <summary>Stable identity: bundle path relative to the extension folder, forward slashes.</summary>
        public string BundleKey { get; set; }
        public string EngineId { get; set; } = "ironpython";
        /// <summary>Module search paths for this script: bundle dir, then extension lib.</summary>
        public List<string> SearchPaths { get; } = new List<string>();
        /// <summary>Parsed bundle.yaml "context:" rule; null = button always enabled.</summary>
        public ContextRule ContextRule { get; set; }
        /// <summary>bundle.yaml "min_host_version:" (a year like 2024); null = no floor.</summary>
        public int? MinHostYear { get; set; }
        /// <summary>bundle.yaml "max_host_version:"; null = no ceiling.</summary>
        public int? MaxHostYear { get; set; }
        /// <summary>*.nobutton: participates in shortcuts and hooks but never renders.</summary>
        public bool NoUi { get; set; }
        /// <summary>*.smartbutton: script.py also runs at ribbon build with __selfinit__ True.</summary>
        public bool IsSmart { get; set; }
        /// <summary>*.toggle: icon flips between the on/off art via pynavis.script.set_toggle_state.</summary>
        public bool IsToggle { get; set; }
        public string OnIconPath { get; set; }
        public string OnDarkIconPath { get; set; }
        public string OffIconPath { get; set; }
        public string OffDarkIconPath { get; set; }
    }

    /// <summary>*.urlbutton: a ribbon button that opens a URL (docs, intranet).</summary>
    public class UrlButtonModel : CaptionedPanelItem
    {
        public string Tooltip { get; set; }
        public string Url { get; set; }
        public string Directory { get; set; }
        public string IconPath { get; set; }
        public string DarkIconPath { get; set; }
        public string SmallIconPath { get; set; }
        public string SmallDarkIconPath { get; set; }
        public string KeyTipOverride { get; set; }
    }

    /// <summary>*.linkbutton: a ribbon button that executes another Navisworks add-in plugin.</summary>
    public class LinkButtonModel : CaptionedPanelItem
    {
        public string Tooltip { get; set; }
        public string PluginId { get; set; }
        public string Directory { get; set; }
        public string IconPath { get; set; }
        public string DarkIconPath { get; set; }
        public string SmallIconPath { get; set; }
        public string SmallDarkIconPath { get; set; }
        public string KeyTipOverride { get; set; }
    }

    /// <summary>*.dockpane: a Navisworks dock pane whose content is pane.xaml, opened
    /// by a ribbon toggle. Claims one of the loader's pane slots at build time.</summary>
    public class DockPaneModel : CaptionedPanelItem
    {
        public string Tooltip { get; set; }
        public string Directory { get; set; }
        /// <summary>pane.xaml in the bundle folder; a dockpane without one is skipped.</summary>
        public string XamlPath { get; set; }
        /// <summary>script.py beside pane.xaml, run once when the pane is built; null if absent.</summary>
        public string ScriptPath { get; set; }
        /// <summary>Stable identity: bundle path relative to the extension folder, forward slashes.</summary>
        public string BundleKey { get; set; }
        public string EngineId { get; set; } = "ironpython";
        public string IconPath { get; set; }
        public string DarkIconPath { get; set; }
        public string SmallIconPath { get; set; }
        public string SmallDarkIconPath { get; set; }
        public string Shortcut { get; set; }
        public string KeyTipOverride { get; set; }
        /// <summary>Module search paths for script.py: bundle dir, then extension lib.</summary>
        public List<string> SearchPaths { get; } = new List<string>();
    }

    /// <summary>An event hook script: hooks\doc-opened.py runs when doc-opened fires.</summary>
    public class HookModel
    {
        public string EventName { get; set; }
        public string ScriptPath { get; set; }
        public string EngineId { get; set; }
        public string ExtensionName { get; set; }
        /// <summary>Module search paths: the hooks folder, then the extension lib.</summary>
        public List<string> SearchPaths { get; } = new List<string>();
        /// <summary>Stable identity for health tracking and toasts.</summary>
        public string Id => ExtensionName + ":" + EventName;
    }
}
