using System.Collections.Generic;
using System.Linq;
using Autodesk.Windows;
using PyNavis.Runtime.Bundles;

namespace PyNavis.Runtime.Ribbon
{
    /// <summary>
    /// Model-to-RibbonItem map for the currently built ribbon. ContextGate flips
    /// IsEnabled through it; toggle and smartbutton refresh find their items here.
    /// Rebuilt on every ribbon Build/Teardown; UI thread only.
    /// </summary>
    public static class ButtonRegistry
    {
        private static readonly List<(PushButtonModel model, RibbonItem item)> Entries =
            new List<(PushButtonModel, RibbonItem)>();

        public static IReadOnlyList<(PushButtonModel model, RibbonItem item)> All => Entries;

        public static void Register(PushButtonModel model, RibbonItem item) => Entries.Add((model, item));

        public static void Clear() { Entries.Clear(); PaneEntries.Clear(); }

        public static RibbonItem Find(string bundleKey) =>
            Entries.FirstOrDefault(e => e.model.BundleKey == bundleKey).item;

        private static readonly List<(DockPaneModel model, RibbonItem item)> PaneEntries =
            new List<(DockPaneModel, RibbonItem)>();

        public static void RegisterPane(DockPaneModel model, RibbonItem item) => PaneEntries.Add((model, item));

        public static (DockPaneModel model, RibbonItem item) FindPane(string bundleKey) =>
            PaneEntries.FirstOrDefault(e => string.Equals(e.model.BundleKey, bundleKey,
                System.StringComparison.OrdinalIgnoreCase));
    }
}
