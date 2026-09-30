using System;
using System.Collections.Generic;
using System.Linq;

namespace PyNavis.Runtime.Bundles
{
    /// <summary>
    /// The ribbon's tabs across every loaded extension: a tab (or a panel inside
    /// it) with the same name in two extensions becomes one. That is how a user's
    /// own extension adds buttons to the pyNavis tab, or to one of its panels,
    /// without editing pyNavis.extension, which every update replaces; the
    /// installer moves anything a user did add there into "My pyNavis.extension"
    /// for exactly this reason.
    ///
    /// Rules, all name-based and case-insensitive:
    ///  - A tab sits where the first extension in load order puts it, so merging
    ///    never reorders the ribbon.
    ///  - Inside a shared tab or panel the shipped pyNavis extension's panels and
    ///    items come first, then each other extension's in load order.
    ///  - The same extension found twice (two roots holding one extension) is
    ///    loaded once, the first copy, and the rest are reported.
    /// Pure: the extensions passed in are never changed.
    /// </summary>
    public static class RibbonMerge
    {
        /// <summary>The extension the installer ships; it leads whatever it shares.</summary>
        public const string ShippedExtension = "pyNavis";

        public static List<TabModel> Tabs(IReadOnlyList<ExtensionModel> extensions, Action<string> note = null)
        {
            var distinct = new List<ExtensionModel>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ext in extensions)
            {
                if (seen.Add(ext.Name ?? "")) distinct.Add(ext);
                else note?.Invoke($"Extension '{ext.Name}' at '{ext.Directory}' is already loaded from an earlier root - skipped.");
            }

            // Where each tab goes: first appearance in load order.
            var order = new List<string>();
            var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ext in distinct)
                foreach (var tab in ext.Tabs)
                    if (placed.Add(tab.Title ?? "")) order.Add(tab.Title ?? "");

            // What goes inside: the shipped extension first, the rest in load order.
            var contributors = distinct.Where(IsShipped).Concat(distinct.Where(e => !IsShipped(e)));
            var tabs = new Dictionary<string, TabModel>(StringComparer.OrdinalIgnoreCase);
            var panels = new Dictionary<TabModel, Dictionary<string, PanelModel>>();
            foreach (var ext in contributors)
            {
                foreach (var tab in ext.Tabs)
                {
                    var key = tab.Title ?? "";
                    if (!tabs.TryGetValue(key, out var merged))
                    {
                        merged = new TabModel { Title = tab.Title, Id = tab.Id };
                        tabs[key] = merged;
                        panels[merged] = new Dictionary<string, PanelModel>(StringComparer.OrdinalIgnoreCase);
                    }
                    foreach (var panel in tab.Panels)
                    {
                        var byTitle = panels[merged];
                        if (!byTitle.TryGetValue(panel.Title ?? "", out var joined))
                        {
                            joined = new PanelModel { Title = panel.Title };
                            byTitle[panel.Title ?? ""] = joined;
                            merged.Panels.Add(joined);
                        }
                        joined.Items.AddRange(panel.Items);
                        joined.Slideout.AddRange(panel.Slideout);
                    }
                }
            }
            return order.Select(title => tabs[title]).ToList();
        }

        private static bool IsShipped(ExtensionModel ext) =>
            string.Equals(ext.Name, ShippedExtension, StringComparison.OrdinalIgnoreCase);
    }
}
