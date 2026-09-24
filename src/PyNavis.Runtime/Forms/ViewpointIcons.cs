using System.Collections.Generic;
using System.Windows.Media;

namespace PyNavis.Runtime.Forms
{
    /// <summary>
    /// Row type glyphs, drawn as geometry (font glyphs sit off-center in a
    /// small box). Identity is carried by SHAPE and every glyph is stroked in
    /// the muted neutral, because the accent is reserved for the primary
    /// action, the selection rail, and the focus ring; a colour per type would
    /// turn a list of thousands into noise. The duplicate warning is the one
    /// coloured glyph, since it flags a problem rather than a category.
    /// </summary>
    public static class ViewpointIcons
    {
        private static readonly Dictionary<string, Geometry> Cache =
            new Dictionary<string, Geometry>();

        // 16x16 coordinate space, stroked (never filled) so one pen fits all.
        private const string Folder =
            "M1.6,4.3 C1.6,3.8 2,3.4 2.5,3.4 L5.6,3.4 L6.9,4.9 L14.5,4.9 " +
            "C15,4.9 15.4,5.3 15.4,5.8 L15.4,12.1 C15.4,12.6 15,13 14.5,13 " +
            "L2.5,13 C2,13 1.6,12.6 1.6,12.1 Z";
        private const string FolderOpen =
            "M1.6,12.1 L1.6,4.3 C1.6,3.8 2,3.4 2.5,3.4 L5.6,3.4 L6.9,4.9 " +
            "L12.9,4.9 C13.4,4.9 13.8,5.3 13.8,5.8 L13.8,7.1 " +
            "M1.6,12.1 L3.5,7.3 L14.7,7.3 L12.8,12.1 C12.7,12.4 12.4,12.6 12,12.6 " +
            "L2.5,12.6 C2,12.6 1.6,12.6 1.6,12.1 Z";
        private const string Viewpoint =
            "M1.5,5.4 L4.4,5.4 L5.5,3.8 L10.5,3.8 L11.6,5.4 L14.5,5.4 L14.5,12.4 " +
            "L1.5,12.4 Z M10.3,8.9 A2.3,2.3 0 1 1 5.7,8.9 A2.3,2.3 0 1 1 10.3,8.9 Z";
        private const string Animation =
            "M2.8,3.4 L13.2,3.4 C13.9,3.4 14.5,4 14.5,4.7 L14.5,11.3 " +
            "C14.5,12 13.9,12.6 13.2,12.6 L2.8,12.6 C2.1,12.6 1.5,12 1.5,11.3 " +
            "L1.5,4.7 C1.5,4 2.1,3.4 2.8,3.4 Z M6.6,6.5 L10.4,8 L6.6,9.5 Z";
        private const string Comment =
            "M2.2,3.6 L13.8,3.6 C14.2,3.6 14.5,3.9 14.5,4.3 L14.5,9.7 " +
            "C14.5,10.1 14.2,10.4 13.8,10.4 L7.6,10.4 L4.5,12.7 L4.5,10.4 " +
            "L2.2,10.4 C1.8,10.4 1.5,10.1 1.5,9.7 L1.5,4.3 C1.5,3.9 1.8,3.6 2.2,3.6 Z";
        private const string Duplicate =
            "M8,2.4 L15,13.6 L1,13.6 Z M8,6.6 L8,9.7 M8,11.4 L8,11.9";

        public static Geometry Get(string name)
        {
            lock (Cache)
            {
                if (Cache.TryGetValue(name, out var cached)) return cached;
                var data = name == "folder" ? Folder
                    : name == "folder-open" ? FolderOpen
                    : name == "animation" ? Animation
                    : name == "comment" ? Comment
                    : name == "duplicate" ? Duplicate
                    : Viewpoint;
                var geometry = Geometry.Parse(data);
                geometry.Freeze();
                Cache[name] = geometry;
                return geometry;
            }
        }

        /// <summary>The glyph for a row's kind.</summary>
        public static Geometry ForRow(ViewpointRow row) =>
            Get(row.IsFolder ? "folder" : row.Kind == "animation" ? "animation" : "viewpoint");
    }
}
