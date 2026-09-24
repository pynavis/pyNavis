using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;

namespace PyNavis.Runtime.Overlay
{
    /// <summary>
    /// The runtime half of the view overlay. The loader's OverlayPlugin (a Navisworks
    /// RenderPlugin, discovered by attribute at startup like the pane slots) forwards
    /// every overlay pass here; scripts add and clear items through pynavis.overlay.
    ///
    /// Items live in world coordinates and are redrawn every frame, so they follow
    /// orbit and zoom for free. Labels are drawn in the window context at the pixel
    /// their world point projects to.
    /// </summary>
    public static class OverlayRegistry
    {
        public const string PluginId = "PyNavis.Overlay.PYNV";

        private static readonly object Gate = new object();
        private static readonly List<OverlayItem> _items = new List<OverlayItem>();
        private static bool _loadAttempted;
        private static bool _loaded;
        private static bool _renderFailureLogged;

        /// <summary>
        /// Whether window-context Y runs bottom-up (OpenGL style) while ProjectPoint
        /// reports top-down pixels. One field so a field check that finds labels
        /// mirrored is a one-line fix.
        /// </summary>
        public static bool FlipY = true;

        public static int Count { get { lock (Gate) return _items.Count; } }

        public static IReadOnlyList<OverlayItem> Items { get { lock (Gate) return _items.ToList(); } }

        /// <summary>Adds an item, replacing any earlier item with the same tag.</summary>
        public static void Add(OverlayItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            lock (Gate)
            {
                if (item.Tag != null) _items.RemoveAll(i => i.Tag == item.Tag);
                _items.Add(item);
            }
        }

        /// <summary>Removes every item with the tag, or every item when tag is null.</summary>
        public static void Clear(string tag)
        {
            lock (Gate)
            {
                if (tag == null) _items.Clear();
                else _items.RemoveAll(i => i.Tag == tag);
            }
        }

        /// <summary>
        /// True when the item is anchored to a measurement that is no longer the
        /// current one: either point missing, or moved by more than a millionth of
        /// a model unit (the API hands back the same doubles frame after frame, so
        /// anything larger is a new measurement, not noise).
        /// </summary>
        public static bool IsStale(OverlayItem item, bool hasFirst, double[] first, bool hasEnd, double[] end)
        {
            if (item.AnchorFirst == null || item.AnchorEnd == null) return false;
            if (!hasFirst || !hasEnd) return true;
            return !Same(item.AnchorFirst, first) || !Same(item.AnchorEnd, end);
        }

        private static bool Same(double[] a, double[] b)
        {
            if (a == null || b == null || a.Length != 3 || b.Length != 3) return false;
            for (var i = 0; i < 3; i++)
                if (Math.Abs(a[i] - b[i]) > 1e-6) return false;
            return true;
        }

        // ---- Navisworks-facing half -------------------------------------------------

        /// <summary>
        /// Loads the overlay plugin once. Navisworks creates plugins lazily, and a
        /// RenderPlugin only receives overlay passes once created, so this runs at
        /// boot and again on first use. False (logged) when the loader shipped
        /// without the plugin, which is what an un-deployed loader looks like.
        /// </summary>
        public static bool EnsureLoaded()
        {
            if (_loaded) return true;
            if (_loadAttempted) return false;
            _loadAttempted = true;
            try
            {
                var record = Application.Plugins.FindPlugin(PluginId);
                if (record == null)
                {
                    Log.Info("Overlay: plugin " + PluginId + " not registered; the loader needs deploying and Navisworks restarting.");
                    return false;
                }
                var plugin = record.IsLoaded ? record.LoadedPlugin : record.LoadPlugin();
                _loaded = plugin != null;
                Log.Info("Overlay: plugin " + (_loaded ? "loaded" : "failed to load") + " (" + record.DisplayName + ").");
            }
            catch (Exception ex)
            {
                Log.Error("Overlay: loading the plugin failed", ex);
            }
            return _loaded;
        }

        /// <summary>Asks the active view for an overlay pass after pending events.</summary>
        public static void Redraw()
        {
            try
            {
                var doc = Application.ActiveDocument;
                if (doc == null) return;
                doc.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.OverlayRender);
            }
            catch (Exception ex)
            {
                Log.Error("Overlay: redraw request failed", ex);
            }
        }

        /// <summary>Called by the loader plugin on every overlay pass of the view.</summary>
        public static void Render(View view, Graphics graphics)
        {
            List<OverlayItem> items;
            lock (Gate)
            {
                if (_items.Count == 0) return;
                DropStale(view);
                items = _items.ToList();
            }
            if (items.Count == 0) return;

            try
            {
                graphics.DepthTest(false);
                graphics.LineWidth(2.0);
                foreach (var item in items)
                    DrawSegments(graphics, item);
                foreach (var item in items)
                    DrawLabel(view, graphics, item);
            }
            catch (Exception ex)
            {
                if (_renderFailureLogged) return;
                _renderFailureLogged = true;
                Log.Error("Overlay: drawing failed (logged once)", ex);
            }
        }

        private static void DropStale(View view)
        {
            if (!_items.Any(i => i.AnchorFirst != null)) return;
            try
            {
                var m = view.Document.CurrentMeasurement;
                var hasFirst = m.HasFirstPoint;
                var hasEnd = m.HasEndPoint;
                var first = hasFirst ? ToArray(m.FirstPoint) : null;
                var end = hasEnd ? ToArray(m.EndPoint) : null;
                _items.RemoveAll(i => IsStale(i, hasFirst, first, hasEnd, end));
            }
            catch (Exception ex)
            {
                // Without a readable measurement an anchored item cannot be judged;
                // keep it rather than flicker it away, and say why once.
                if (_renderFailureLogged) return;
                _renderFailureLogged = true;
                Log.Error("Overlay: reading the current measurement failed (logged once)", ex);
            }
        }

        // Built per call, never as static fields: an API Color needs a live
        // Navisworks, and a static initializer would take the whole store down
        // under the test host (measured, TypeInitializationException).
        private static Color Accent() => new Color(1.0, 0.55, 0.0);
        private static Color Paper() => new Color(1.0, 1.0, 1.0);

        private static void DrawSegments(Graphics graphics, OverlayItem item)
        {
            graphics.Color(Accent(), 1.0);
            foreach (var seg in item.Segments)
            {
                if (seg.From == null || seg.To == null) continue;
                if (seg.Dashed) graphics.LineStipple(3, 0x0F0F);
                graphics.Line(ToPoint(seg.From), ToPoint(seg.To));
                if (seg.Dashed) graphics.LineStipple(1, 0xFFFF);
            }
        }

        private static void DrawLabel(View view, Graphics graphics, OverlayItem item)
        {
            if (string.IsNullOrEmpty(item.Label) || item.LabelAt == null) return;
            var shot = view.ProjectPoint(ToPoint(item.LabelAt), false, false);
            var x = (double)shot.X;
            var y = FlipY ? graphics.WindowHeight - (double)shot.Y : (double)shot.Y;

            graphics.BeginWindowContext();
            try
            {
                using (var font = new TextFontInfo("Segoe UI", 11, 700, false, false))
                {
                    var extents = graphics.Text2DExtents(font, item.Label);
                    var w = extents.Bounds.Max.X - extents.Bounds.Min.X;
                    var h = extents.Bounds.Max.Y - extents.Bounds.Min.Y;
                    const double pad = 5.0;
                    // Box sits just up and right of the anchor pixel, like the native tag.
                    var x0 = x + 8.0;
                    var y0 = y + 8.0;
                    graphics.Color(Accent(), 1.0);
                    graphics.Rectangle(new Point2D(x0, y0), new Point2D(x0 + w + 2 * pad, y0 + h + 2 * pad), true);
                    graphics.Color(Paper(), 1.0);
                    graphics.Text2D(font, item.Label, new Point2D(x0 + pad, y0 + pad), 0, 0);
                }
            }
            finally
            {
                graphics.EndWindowContext();
            }
        }

        private static Point3D ToPoint(double[] p) => new Point3D(p[0], p[1], p[2]);

        private static double[] ToArray(Point3D p) => new[] { p.X, p.Y, p.Z };
    }
}
