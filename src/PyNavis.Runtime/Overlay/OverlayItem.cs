using System.Collections.Generic;

namespace PyNavis.Runtime.Overlay
{
    /// <summary>
    /// One straight piece of an overlay item, in world coordinates. Plain doubles
    /// rather than API points so the store is testable without Navisworks; the
    /// renderer converts while drawing.
    /// </summary>
    public sealed class OverlaySegment
    {
        public double[] From { get; set; }
        public double[] To { get; set; }
        public bool Dashed { get; set; }
    }

    /// <summary>
    /// Something a tool asked to have drawn in the 3D view: a few line segments and
    /// an optional screen-facing label. An item anchored to the two points of the
    /// native measurement removes itself the first frame that measurement no
    /// longer matches, which is how Face Distance's dimension clears when the user
    /// measures something else without any event wiring.
    /// </summary>
    public sealed class OverlayItem
    {
        public string Tag { get; set; }
        public List<OverlaySegment> Segments { get; } = new List<OverlaySegment>();
        public string Label { get; set; }
        /// <summary>World point the label is pinned to, or null for no label.</summary>
        public double[] LabelAt { get; set; }
        /// <summary>Measurement points this item belongs to, or null when free-standing.</summary>
        public double[] AnchorFirst { get; set; }
        public double[] AnchorEnd { get; set; }
    }
}
