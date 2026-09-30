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
        /// wrapped ribbon form on a large button, then the Shift+Click marker when the
        /// bundle has a config.py and the shortcut marker when a chord resolves to it.</summary>
        public static string Text(PushButtonModel model, bool small, bool on, bool hasChord)
        {
            var useOn = on && model.IsToggle && model.TitleOn != null;
            var text = useOn
                ? (small ? model.TitleOn : model.RibbonTitleOn)
                : (small ? model.Title : model.RibbonTitle);
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
            if (chord != null)
                tooltip = string.IsNullOrEmpty(tooltip) ? $"({chord})" : $"{tooltip} ({chord})";
            return string.IsNullOrEmpty(tooltip) ? null : tooltip;
        }
    }
}
