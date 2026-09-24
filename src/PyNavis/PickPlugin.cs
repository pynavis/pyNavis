using System;
using System.Runtime.CompilerServices;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;

namespace PyNavis
{
    /// <summary>
    /// The compile-time half of pynavis.pick. Navisworks discovers tool plugins by
    /// scanning attributed types in the Plugins folder at startup, so the plugin
    /// must exist here in the loader; every decision comes from the runtime's
    /// PickService, exactly like OverlayPlugin delegating to OverlayRegistry.
    ///
    /// PluginAttribute is all a ToolPlugin needs to be discovered: unlike a dock
    /// pane it carries no second attribute, and unlike a command it never appears
    /// in the ribbon. The runtime loads it by id when a script starts a pick.
    ///
    /// Handlers that pyNavis does not consume are deliberately not overridden:
    /// the base returns false, which leaves the host its own wheel zoom, middle
    /// button pan and drag navigation, so the user can still reach the face they
    /// mean while a pick is waiting.
    /// </summary>
    [Plugin("PyNavis.Pick", "PYNV",
        DisplayName = "pyNavis pick",
        ToolTip = "Picks a point in the view for a pyNavis tool")]
    public class PickPlugin : ToolPlugin
    {
        private static bool _failureLogged;

        static PickPlugin()
        {
            AssemblyResolver.Install();
        }

        public override bool MouseDown(View view, KeyModifiers modifiers, ushort button, int x, int y, double timeOffset)
        {
            try { return ForwardMouseDown(view, button, x, y); }
            catch (Exception ex) { Fail("Pick mouse down failed. ", ex); return false; }
        }

        public override bool MouseMove(View view, KeyModifiers modifiers, int x, int y, double timeOffset)
        {
            try { return ForwardMouseMove(view, x, y); }
            catch (Exception ex) { Fail("Pick mouse move failed. ", ex); return false; }
        }

        public override bool MouseLeave(View view, double timeOffset)
        {
            try { return ForwardMouseLeave(view); }
            catch (Exception ex) { Fail("Pick mouse leave failed. ", ex); return false; }
        }

        public override bool KeyDown(View view, KeyModifiers modifier, ushort key, double timeOffset)
        {
            try { return ForwardKeyDown(view, key); }
            catch (Exception ex) { Fail("Pick key down failed. ", ex); return false; }
        }

        public override bool ContextMenu(View view, int x, int y)
        {
            try { return ForwardContextMenu(view, x, y); }
            catch (Exception ex) { Fail("Pick context menu failed. ", ex); return false; }
        }

        public override Cursor GetCursor(View view, KeyModifiers modifier)
        {
            try { return ForwardGetCursor(); }
            catch (Exception ex) { Fail("Pick cursor failed. ", ex); return Cursor.Unhandled; }
        }

        public override void OverlayRender(View view, Graphics graphics)
        {
            try { ForwardRender(view, graphics); }
            catch (Exception ex) { Fail("Pick render failed. ", ex); }
        }

        /// <summary>Logged once: mouse moves and overlay passes arrive at redraw
        /// speed, and a broken runtime would otherwise fill loader.log.</summary>
        private static void Fail(string what, Exception ex)
        {
            if (_failureLogged) return;
            _failureLogged = true;
            LoaderLog.Error(what + ex);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool ForwardMouseDown(View view, ushort button, int x, int y)
        {
            return Runtime.Pick.PickService.MouseDown(view, button, x, y);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool ForwardMouseMove(View view, int x, int y)
        {
            return Runtime.Pick.PickService.MouseMove(view, x, y);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool ForwardMouseLeave(View view)
        {
            return Runtime.Pick.PickService.MouseLeave(view);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool ForwardKeyDown(View view, ushort key)
        {
            return Runtime.Pick.PickService.KeyDown(view, key);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool ForwardContextMenu(View view, int x, int y)
        {
            return Runtime.Pick.PickService.ContextMenu(view, x, y);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Cursor ForwardGetCursor()
        {
            return Runtime.Pick.PickService.GetCursor();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ForwardRender(View view, Graphics graphics)
        {
            Runtime.Pick.PickService.Render(view, graphics);
        }
    }
}
