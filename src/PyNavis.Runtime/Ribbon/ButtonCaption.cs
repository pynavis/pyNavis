using PyNavis.Runtime.Bundles;

namespace PyNavis.Runtime.Ribbon
{
    /// <summary>
    /// What a pushbutton's caption and tooltip say. The ribbon build and a toggle's
    /// state change both read it, so a toggle keeps its on-state wording across a
    /// Reload instead of being redrawn under its resting name. Pure.
    /// </summary>
    public static class ButtonCaption
    {
        /// <summary>The caption: the on-state title while a toggle with one is on, the
        /// wrapped ribbon form on a large button, then the experimental marker when the
        /// bundle says so, the Shift+Click marker when the
        /// bundle has a config.py and the shortcut marker when a chord resolves to it.</summary>
        public static string Text(PushButtonModel model, bool small, bool on, bool hasChord)
        {
            var useOn = on && model.IsToggle && model.TitleOn != null;
            var text = useOn
                ? (small ? model.TitleOn : model.RibbonTitleOn)
                : (small ? model.Title : model.RibbonTitle);
            if (model.IsExperimental)
                text = RibbonMarkers.WithExperimentalMarker(text);
            if (model.ConfigScriptPath != null)
                text = RibbonMarkers.WithConfigMarker(text);
            if (hasChord)
                text = RibbonMarkers.WithShortcutMarker(text);
            return text;
        }

        /// <summary>The tooltip for the state, naming the RESOLVED chord (so a rebound
        /// tool shows the user's key), or null when there is nothing to say.</summary>
        public static string Tooltip(PushButtonModel model, bool on, string chord)
        {
            var tooltip = on && model.IsToggle && model.TooltipOn != null ? model.TooltipOn : model.Tooltip;
            tooltip = WithStatus(model, tooltip);
            if (chord != null)
                tooltip = string.IsNullOrEmpty(tooltip) ? $"({chord})" : $"{tooltip} ({chord})";
            return string.IsNullOrEmpty(tooltip) ? null : tooltip;
        }

        /// <summary>The ribbon caption of a pulldown, url, link or dock pane button: the
        /// wrapped title plus the experimental marker when the bundle carries one.</summary>
        public static string Caption(CaptionedPanelItem item) => Caption(item, item.RibbonTitle);

        /// <summary>A caption a script chose (a smartbutton's __button__.Title), with the
        /// experimental marker kept so selfinit cannot drop it.</summary>
        public static string Caption(CaptionedPanelItem item, string text) =>
            item.IsExperimental && !HasExperimentalMarker(text)
                ? RibbonMarkers.WithExperimentalMarker(text) : text;

        /// <summary>True when the marker already stands as a word of its own in the
        /// caption: a script that reads __button__.Title back and writes it again.</summary>
        private static bool HasExperimentalMarker(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            var marker = " " + RibbonMarkers.ExperimentalMarker;
            return text.EndsWith(marker, System.StringComparison.Ordinal)
                || text.Contains(marker + " ")
                || text.Contains(marker + "\n");
        }

        /// <summary>The tooltip of a pulldown, url, link or dock pane button, or null
        /// when there is nothing to say.</summary>
        public static string Tooltip(CaptionedPanelItem item, string tooltip)
        {
            tooltip = WithStatus(item, tooltip);
            return string.IsNullOrEmpty(tooltip) ? null : tooltip;
        }

        /// <summary>"Experimental: " ahead of an experimental bundle's tooltip, or the
        /// bare word when it has none. Anything else passes through untouched.</summary>
        private static string WithStatus(CaptionedPanelItem item, string tooltip)
        {
            if (!item.IsExperimental) return tooltip;
            return string.IsNullOrEmpty(tooltip) ? "Experimental" : "Experimental: " + tooltip;
        }
    }
}
