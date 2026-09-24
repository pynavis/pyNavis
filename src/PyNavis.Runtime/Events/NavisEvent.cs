using System.Collections.Generic;

namespace PyNavis.Runtime.Events
{
    /// <summary>Normalized pyNavis events; hooks and gating consume these, never raw API events.</summary>
    public enum NavisEvent
    {
        DocOpened, DocClosed, DocSaved,
        ModelAppended, ModelRemoved,
        SelectionChanged, SelectionSetsChanged, ViewpointsChanged,
        ViewpointRecalled, CameraMoved,
        AppInit, AppClosing,
        BeforeCommand, AfterCommand,
    }

    /// <summary>Kebab-case wire names: hook filenames and __event__["name"] use these.</summary>
    public static class EventNames
    {
        private static readonly Dictionary<NavisEvent, string> Names = new Dictionary<NavisEvent, string>
        {
            [NavisEvent.DocOpened] = "doc-opened",
            [NavisEvent.DocClosed] = "doc-closed",
            [NavisEvent.DocSaved] = "doc-saved",
            [NavisEvent.ModelAppended] = "model-appended",
            [NavisEvent.ModelRemoved] = "model-removed",
            [NavisEvent.SelectionChanged] = "selection-changed",
            [NavisEvent.SelectionSetsChanged] = "selection-sets-changed",
            // Three viewpoint events, deliberately named apart. viewpoints-changed is the
            // saved-viewpoints TREE being edited; viewpoint-recalled is a saved view being
            // activated; camera-moved is the view moving under navigation.
            [NavisEvent.ViewpointsChanged] = "viewpoints-changed",
            [NavisEvent.ViewpointRecalled] = "viewpoint-recalled",
            [NavisEvent.CameraMoved] = "camera-moved",
            [NavisEvent.AppInit] = "app-init",
            [NavisEvent.AppClosing] = "app-closing",
            [NavisEvent.BeforeCommand] = "before-command",
            [NavisEvent.AfterCommand] = "after-command",
        };

        private static readonly Dictionary<string, NavisEvent> ByName = BuildInverse();

        private static Dictionary<string, NavisEvent> BuildInverse()
        {
            var map = new Dictionary<string, NavisEvent>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var pair in Names) map[pair.Value] = pair.Key;
            return map;
        }

        public static string NameOf(NavisEvent evt) => Names[evt];

        public static bool TryParse(string name, out NavisEvent evt)
        {
            if (name != null) return ByName.TryGetValue(name, out evt);
            evt = default(NavisEvent);
            return false;
        }

        public static IEnumerable<string> All => Names.Values;
    }

    public class NavisEventArgs
    {
        public NavisEvent Event { get; set; }
        public string Name => EventNames.NameOf(Event);
        /// <summary>Bundle key of the command for Before/AfterCommand; null otherwise.</summary>
        public string CommandKey { get; set; }

        /// <summary>Display name of the saved viewpoint for ViewpointRecalled; null
        /// otherwise. Captured when the event is raised rather than read back by the hook,
        /// which would see whatever is current by the time the script runs.</summary>
        public string ViewpointName { get; set; }
    }
}
