using System;
using System.Windows.Threading;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.DocumentParts;
using Autodesk.Navisworks.Api.Plugins;

namespace PyNavis.Runtime.Pick
{
    /// <summary>
    /// The Navisworks half of pynavis.pick. The loader's PickPlugin (a ToolPlugin,
    /// discovered by attribute at startup like the overlay and the pane slots)
    /// forwards every mouse, key and overlay callback here; scripts drive it
    /// through pynavis.pick, which waits on PickSession.Ended.
    ///
    /// Only the active session is remembered, because the document has exactly
    /// one tool: a second Begin supersedes the first rather than queueing behind
    /// it, so a script that blocks on a pick and is then clicked again ends its
    /// first pick cleanly instead of stacking two live tools.
    /// </summary>
    public static class PickService
    {
        public const string PluginId = "PyNavis.Pick.PYNV";

        /// <summary>
        /// Half-width of the pick region in pixels. The API doc says "normally 1",
        /// but 1 means a snap only fires when the cursor is exactly on the vertex;
        /// the native measure tool clearly snaps from a few pixels away, and this
        /// is the tolerance that buys.
        /// </summary>
        public const int PickRadius = 5;

        /// <summary>
        /// asRendered. The API doc says "whether picking should operate only on
        /// items that were rendered during the last frame. Normally true", and
        /// true is also what the user means: they are clicking what they can see.
        /// </summary>
        public const bool AsRendered = true;

        /// <summary>Escape's virtual key code. KeyDown's key parameter is
        /// undocumented, so the first key of every session is logged and this
        /// assumption can be checked against a field log.</summary>
        public const ushort EscapeKey = 27;

        /// <summary>MouseDown's button parameter is undocumented too. These are
        /// the Win32 MK_LBUTTON / MK_RBUTTON values, which is what a mouse
        /// message's wParam carries; anything else (middle, side buttons) is left
        /// unhandled so the host keeps its own pan and zoom.</summary>
        public const ushort LeftButton = 1;
        public const ushort RightButton = 2;

        private static PickSession _current;
        private static DocumentTool _tool;
        private static Tool _previousTool;
        private static bool _toolHeld;
        private static bool _settingTool;
        private static bool _buttonLogged;
        private static bool _keyLogged;

        // ---- TEMPORARY diagnostics (every line prefixed "Pick diag:") ----------------
        // Evidence for the mirrored hover marker: is the DRAW mirrored (window context
        // Y direction) or is the PICK at the wrong pixel (mouse coordinates in another
        // space or scale)? Delete this block and its three call sites to remove.
        private static int _diagMouseX = int.MinValue;
        private static int _diagMouseY = int.MinValue;
        private static DateTime _diagLastRender = DateTime.MinValue;
        private static bool _diagRenderFailed;
        private static bool _diagClickFailed;

        /// <summary>
        /// Puts the document into the pick tool and returns the session to wait
        /// on, or null when the pick plugin is not registered, which is what a
        /// loader that has not been deployed looks like (same contract as
        /// OverlayRegistry.EnsureLoaded).
        /// </summary>
        public static PickSession Begin(string prompt)
        {
            try
            {
                Supersede();

                var plugin = FindPlugin();
                if (plugin == null) return null;

                var doc = Application.ActiveDocument;
                if (doc == null)
                {
                    Log.Info("Pick: no active document.");
                    return null;
                }

                _tool = doc.Tool;
                // Only the FIRST pick in a chain reads the tool to go back to.
                // Superseding one pick with another leaves our own tool current
                // and its restore still queued, so re-reading here would record
                // the pick tool instead of whatever the user was using.
                if (!_toolHeld)
                {
                    _previousTool = _tool.Value;
                    // Restoring to CustomToolPlugin is rejected by the API and
                    // would mean handing the user back somebody else's tool.
                    if (_previousTool == Tool.CustomToolPlugin) _previousTool = Tool.Select;
                }
                _toolHeld = true;

                var session = new PickSession(prompt);
                session.Ended += OnEnded;
                _current = session;
                _buttonLogged = false;
                _keyLogged = false;
                _diagMouseX = int.MinValue;
                _diagMouseY = int.MinValue;
                _diagLastRender = DateTime.MinValue;
                DiagDpi();
                session.Activate();

                _tool.Changed += OnToolChanged;
                SetTool(() => _tool.SetCustomToolPlugin(plugin));
                Log.Info("Pick: started, previous tool " + _previousTool
                    + ", primitives " + PrimitivesText() + ", radius " + PickRadius + ".");
                return session;
            }
            catch (Exception ex)
            {
                Log.Error("Pick: starting failed", ex);
                var session = _current;
                if (session != null) session.Cancel(PickCancelReason.Error);
                return null;
            }
        }

        /// <summary>Ends whatever is running, used by Begin and by a script that
        /// gives up without waiting.</summary>
        public static void CancelCurrent(PickCancelReason reason)
        {
            var session = _current;
            if (session != null) session.Cancel(reason);
        }

        private static void Supersede()
        {
            var session = _current;
            if (session != null) session.Cancel(PickCancelReason.Superseded);
        }

        private static ToolPlugin FindPlugin()
        {
            var record = Application.Plugins.FindPlugin(PluginId);
            if (record == null)
            {
                Log.Info("Pick: plugin " + PluginId + " not registered; the loader needs deploying and Navisworks restarting.");
                return null;
            }
            var plugin = (record.IsLoaded ? record.LoadedPlugin : record.LoadPlugin()) as ToolPlugin;
            if (plugin == null) Log.Info("Pick: plugin " + PluginId + " failed to load.");
            return plugin;
        }

        // ---- ending -----------------------------------------------------------------

        private static void OnEnded(object sender, EventArgs e)
        {
            var session = sender as PickSession;
            if (session == null || !ReferenceEquals(session, _current)) return;
            _current = null;

            var tool = _tool;
            _tool = null;
            try
            {
                if (tool != null) tool.Changed -= OnToolChanged;
            }
            catch (Exception ex)
            {
                Log.Error("Pick: unsubscribing from the tool failed", ex);
            }

            Log.Info("Pick: ended " + session.State
                + (session.State == PickState.Cancelled ? " (" + session.CancelReason + ")" : "") + ".");

            // The user already chose the tool they want, so putting one back
            // would fight them; every other ending owes them the tool they had.
            if (session.CancelReason == PickCancelReason.ToolChanged) _toolHeld = false;
            else Restore(tool);
            Redraw();
        }

        /// <summary>
        /// Puts the previous tool back, but off the input callback that asked for
        /// it: Complete and Cancel both run inside the tool's own MouseDown or
        /// KeyDown, and swapping the document's tool while the host is still
        /// dispatching input to it is the kind of re-entrancy that takes the host
        /// with it. Background priority also means the restore lands after a
        /// blocking script's nested dispatcher frame has already returned.
        /// </summary>
        private static void Restore(DocumentTool tool)
        {
            if (tool == null) { _toolHeld = false; return; }
            var previous = _previousTool;
            try
            {
                var dispatcher = System.Windows.Application.Current != null
                    ? System.Windows.Application.Current.Dispatcher
                    : Dispatcher.CurrentDispatcher;
                dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)(() =>
                {
                    // A pick that superseded this one is holding the tool now and
                    // owns the restore; putting the old tool back would cancel it.
                    if (_current != null) return;
                    try
                    {
                        _toolHeld = false;
                        SetTool(() => tool.Set(previous));
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Pick: restoring the tool failed", ex);
                    }
                }));
            }
            catch (Exception ex)
            {
                Log.Error("Pick: scheduling the tool restore failed", ex);
            }
        }

        /// <summary>Runs a tool change with Changed suppressed, so our own set
        /// and restore do not read as the user choosing another tool.</summary>
        private static void SetTool(Action change)
        {
            _settingTool = true;
            try { change(); }
            finally { _settingTool = false; }
        }

        private static void OnToolChanged(object sender, EventArgs e)
        {
            if (_settingTool) return;
            var session = _current;
            if (session == null) return;
            try
            {
                // Still ours: the host re-announced the same tool, not a user choice.
                var tool = _tool;
                if (tool != null && tool.Value == Tool.CustomToolPlugin
                    && tool.CustomToolPluginId == PluginId) return;
            }
            catch (Exception ex)
            {
                Log.Error("Pick: reading the current tool failed", ex);
            }
            session.Cancel(PickCancelReason.ToolChanged);
        }

        // ---- tool plugin callbacks --------------------------------------------------

        /// <summary>
        /// Snap to anything the native measure tool snaps to, plus the surface
        /// point when nothing is near: triangle vertices and edges, polyline
        /// vertices and midpoints, arc centres. Point asks for the picked point
        /// itself, which is the whole answer here. Distance, NearThreshold,
        /// Center and CenterExactSurface are left out: they change WHICH surface
        /// wins, and the user is entitled to the one under the cursor.
        /// Built per call, never a static field, because an API enum field would
        /// pull Navisworks into the type initializer.
        /// </summary>
#if NAVIS_PICK_SNAPS
        private static PickPrimitives Primitives()
        {
            return PickPrimitives.Vertex | PickPrimitives.Edge | PickPrimitives.LineVertex
                | PickPrimitives.LineMiddle | PickPrimitives.ArcCenter | PickPrimitives.Point;
        }

        private static string PrimitivesText() => ((int)Primitives()).ToString();
#else
        private static string PrimitivesText() => "surface only (this release has no snap API)";
#endif

        /// <summary>The raw pick, flattened so the rest of the service is the same on
        /// every release. Normal and ResultBits only exist from Navisworks 2025 on;
        /// earlier releases return the surface point with no snap and no normal.</summary>
        private struct RawHit
        {
            public Point3D Point;
            public double[] Normal;
            public int ResultBits;
            public ModelItem Item;
        }

        private static bool TryPick(View view, int x, int y, out RawHit raw)
        {
#if NAVIS_PICK_SNAPS
            var hit = view.PickItemFromPoint(x, y, PickRadius, AsRendered, Primitives());
            if (hit == null || hit.ModelItem == null) { raw = default(RawHit); return false; }
            raw = new RawHit
            {
                Point = hit.Point, Normal = ToArray(hit.Normal),
                ResultBits = (int)hit.ResultBits, Item = hit.ModelItem,
            };
#else
            var hit = view.PickItemFromPoint(x, y, PickRadius, AsRendered);
            if (hit == null || hit.ModelItem == null) { raw = default(RawHit); return false; }
            raw = new RawHit { Point = hit.Point, Normal = null, ResultBits = 0, Item = hit.ModelItem };
#endif
            return true;
        }

        public static bool MouseMove(View view, int x, int y)
        {
            var session = _current;
            if (session == null || view == null) return false;

            // Diagnostics only: the raw pixel the host handed us, read back by Render.
            _diagMouseX = x;
            _diagMouseY = y;

            RawHit hit;
            if (!TryPick(view, x, y, out hit))
            {
                if (session.ClearHover()) Redraw();
                return true;
            }
            // No logging and no allocation beyond the pick itself: this runs on
            // every mouse move, and the redraw only follows a real change.
            if (session.Hover(ToArray(hit.Point), hit.Normal,
                              PickSnaps.From(hit.ResultBits))) Redraw();
            return true;
        }

        public static bool MouseDown(View view, ushort button, int x, int y)
        {
            var session = _current;
            if (session == null || view == null) return false;

            if (button == RightButton)
            {
                session.Cancel(PickCancelReason.RightClick);
                return true;
            }
            if (button != LeftButton)
            {
                // Middle and side buttons stay with the host, so pan and zoom
                // keep working while the user reaches the face they want.
                if (!_buttonLogged)
                {
                    _buttonLogged = true;
                    Log.Info("Pick: unhandled mouse button " + button + " passed to the host.");
                }
                return false;
            }

            // A fresh pick at the click pixel, never the stored hover: the hover
            // is whatever the last MouseMove saw, and a click can arrive after a
            // move the tool never got (a tap on a touchpad, a click straight
            // after the view finished redrawing).
            RawHit hit;
            if (!TryPick(view, x, y, out hit)) return true;   // empty space: keep waiting

            var snap = PickSnaps.From(hit.ResultBits);
            var point = ToArray(hit.Point);
            Log.Info("Pick: hit pixel (" + x + ", " + y + ") point ("
                + Text(point) + ") resultBits " + hit.ResultBits + " snap " + snap + ".");
            DiagClick(view, x, y, hit.Point);
            session.Complete(new PickHit
            {
                Point = point,
                Normal = hit.Normal,
                Snap = snap,
                Item = hit.Item,
            });
            return true;
        }

        public static bool MouseLeave(View view)
        {
            var session = _current;
            if (session == null) return false;
            if (session.ClearHover()) Redraw();
            return false;   // leaving the view is not an action the tool consumes
        }

        public static bool KeyDown(View view, ushort key)
        {
            var session = _current;
            if (session == null) return false;
            if (!_keyLogged)
            {
                _keyLogged = true;
                Log.Info("Pick: first key code " + key + " (Escape is assumed to be " + EscapeKey + ").");
            }
            if (key != EscapeKey) return false;
            session.Cancel(PickCancelReason.Escape);
            return true;
        }

        public static bool ContextMenu(View view, int x, int y)
        {
            var session = _current;
            if (session == null) return false;
            session.Cancel(PickCancelReason.RightClick);
            return true;   // handled, so no menu opens over a pick that just ended
        }

        public static Cursor GetCursor()
        {
            var session = _current;
            if (session == null) return Cursor.Unhandled;
            if (PickSnaps.IsPointSnap(session.HoverSnap)) return Cursor.MeasureVertex;
            if (session.HoverSnap == PickSnap.Edge) return Cursor.MeasureEdge;
            return Cursor.Measure;
        }

        // ---- the hover marker -------------------------------------------------------

        /// <summary>Half-size of the marker in pixels: big enough to find against
        /// a busy model, small enough not to cover the point it marks.</summary>
        private const double MarkerPixels = 7.0;

        // Same accent as the view overlay, built per call for the same reason.
        private static Color Accent() { return new Color(1.0, 0.55, 0.0); }

        public static void Render(View view, Graphics graphics)
        {
            var session = _current;
            if (session == null || !session.HasHover || view == null || graphics == null) return;

            var p = session.HoverPoint;
            var shot = view.ProjectPoint(new Point3D(p[0], p[1], p[2]), false, false);
            var x = (double)shot.X;
            var y = Overlay.OverlayRegistry.FlipY
                ? graphics.WindowHeight - (double)shot.Y : (double)shot.Y;

            DiagRender(view, graphics, p, shot, x, y);

            graphics.DepthTest(false);
            graphics.LineWidth(2.0);
            graphics.BeginWindowContext();
            try
            {
                graphics.Color(Accent(), 1.0);
                DrawMarker(graphics, x, y, session.HoverSnap);
            }
            finally
            {
                graphics.EndWindowContext();
            }
        }

        /// <summary>
        /// Three shapes, so the snap is readable without watching the cursor:
        /// a square on an exact point, a triangle on an edge, a ring on a plain
        /// face. All are sized in pixels, so the marker stays the same size
        /// however far away the geometry is.
        /// </summary>
        private static void DrawMarker(Graphics graphics, double x, double y, PickSnap snap)
        {
            const double r = MarkerPixels;
            if (PickSnaps.IsPointSnap(snap))
                graphics.Rectangle(new Point2D(x - r, y - r), new Point2D(x + r, y + r), false);
            else if (snap == PickSnap.Edge)
                graphics.Triangle(new Point2D(x, y + r), new Point2D(x - r, y - r),
                                  new Point2D(x + r, y - r), false);
            else
                graphics.Circle(new Point2D(x, y), r, false);

            // A short cross through the middle: the shapes say what was snapped
            // to, this says exactly where.
            const double arm = 3.0;
            graphics.Line(new Point2D(x - arm, y), new Point2D(x + arm, y));
            graphics.Line(new Point2D(x, y - arm), new Point2D(x, y + arm));
        }

        private static void Redraw()
        {
            try
            {
                var doc = Application.ActiveDocument;
                if (doc == null) return;
                doc.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.OverlayRender);
            }
            catch (Exception ex)
            {
                Log.Error("Pick: redraw request failed", ex);
            }
        }

        // ---- TEMPORARY diagnostics --------------------------------------------------
        // Three probes, all prefixed "Pick diag:", all swallowing their own failures so
        // a broken probe can never break a pick. Delete this region and its call sites
        // in Begin, MouseMove, MouseDown and Render to remove them.

        /// <summary>Once per second while a session is drawing: the raw mouse pixel,
        /// the hover world point, its projection, both reported window sizes and the
        /// pixel actually drawn. If the drawn Y mirrors the mouse Y about the window
        /// height, the DRAW is flipped; if shot.Y already disagrees with the mouse Y,
        /// the PICK was taken at the wrong pixel.</summary>
        private static void DiagRender(View view, Graphics graphics, double[] p,
                                       ProjectionResult shot, double x, double y)
        {
            try
            {
                var now = DateTime.UtcNow;
                if ((now - _diagLastRender).TotalMilliseconds < 1000.0) return;
                _diagLastRender = now;

                Log.Info("Pick diag: render mouse (" + _diagMouseX + ", " + _diagMouseY + ")"
                    + " world (" + Text(p) + ")"
                    + " shot (" + shot.X + ", " + shot.Y + ") depth " + shot.Depth.ToString("0.#####")
                    + " graphics window (" + graphics.WindowWidth + " x " + graphics.WindowHeight + ")"
                    + " view (" + view.Width + " x " + view.Height + ")"
                    + " drawn (" + x.ToString("0.##") + ", " + y.ToString("0.##") + ")"
                    + " flipY " + Overlay.OverlayRegistry.FlipY + ".");
            }
            catch (Exception ex)
            {
                if (_diagRenderFailed) return;
                _diagRenderFailed = true;
                Log.Error("Pick diag: render probe failed", ex);
            }
        }

        /// <summary>On a completed pick: the click pixel and where the picked world
        /// point projects back to. Equal (within the pick radius) means the mouse
        /// coordinates and ProjectPoint share one coordinate system and one scale.</summary>
        private static void DiagClick(View view, int x, int y, Point3D world)
        {
            try
            {
                var back = view.ProjectPoint(world, false, false);
                Log.Info("Pick diag: click (" + x + ", " + y + ") projects back to ("
                    + back.X + ", " + back.Y + ") depth " + back.Depth.ToString("0.#####")
                    + " delta (" + (back.X - x) + ", " + (back.Y - y) + ")"
                    + " view (" + view.Width + " x " + view.Height + ").");
            }
            catch (Exception ex)
            {
                if (_diagClickFailed) return;
                _diagClickFailed = true;
                Log.Error("Pick diag: click probe failed", ex);
            }
        }

        /// <summary>Once per session: the DPI scale, in case the mouse pixels and the
        /// projected pixels differ by a scale factor rather than a flip.</summary>
        private static void DiagDpi()
        {
            try
            {
                var app = System.Windows.Application.Current;
                var window = app != null ? app.MainWindow : null;
                if (window != null)
                {
                    var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
                    Log.Info("Pick diag: WPF DPI scale " + dpi.DpiScaleX + " x " + dpi.DpiScaleY
                        + " (" + dpi.PixelsPerInchX + " x " + dpi.PixelsPerInchY + " dpi),"
                        + " main window " + window.ActualWidth + " x " + window.ActualHeight + ".");
                    return;
                }
                using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                    Log.Info("Pick diag: desktop DPI " + g.DpiX + " x " + g.DpiY
                        + " (no WPF main window).");
            }
            catch (Exception ex)
            {
                Log.Error("Pick diag: DPI probe failed", ex);
            }
        }

        // ---- end TEMPORARY diagnostics ----------------------------------------------

        private static double[] ToArray(Point3D p)
        {
            return p == null ? null : new[] { p.X, p.Y, p.Z };
        }

        private static double[] ToArray(UnitVector3D v)
        {
            return v == null ? null : new[] { v.X, v.Y, v.Z };
        }

        private static string Text(double[] p)
        {
            if (p == null) return "";
            return p[0].ToString("0.###") + ", " + p[1].ToString("0.###") + ", " + p[2].ToString("0.###");
        }
    }
}
