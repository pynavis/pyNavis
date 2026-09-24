using System;
using System.Runtime.CompilerServices;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;

namespace PyNavis
{
    /// <summary>
    /// The compile-time half of the pyNavis view overlay. Navisworks discovers render
    /// plugins by scanning attributed types in the Plugins folder at startup, so the
    /// plugin must exist here in the loader; everything drawn comes from the runtime's
    /// OverlayRegistry, exactly like the pane slots delegating to PaneRegistry.
    ///
    /// A RenderPlugin only receives passes once created, and Navisworks creates plugins
    /// lazily, so the runtime loads this one by id at boot (OverlayRegistry.EnsureLoaded).
    /// </summary>
    [Plugin("PyNavis.Overlay", "PYNV",
        DisplayName = "pyNavis overlay",
        ToolTip = "Draws pyNavis dimensions and marks in the view")]
    public class OverlayPlugin : RenderPlugin
    {
        private static bool _failureLogged;

        static OverlayPlugin()
        {
            AssemblyResolver.Install();
        }

        public override void OverlayRender(View view, Graphics graphics)
        {
            try
            {
                Forward(view, graphics);
            }
            catch (Exception ex)
            {
                // Logged once: this runs on every frame, and a broken runtime would
                // otherwise fill loader.log at redraw speed.
                if (_failureLogged) return;
                _failureLogged = true;
                LoaderLog.Error("Overlay render failed. " + ex);
            }
        }

        public override void Render(View view, Graphics graphics)
        {
            // Nothing in the main pass: overlay items draw on top of the model.
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Forward(View view, Graphics graphics)
        {
            Runtime.Overlay.OverlayRegistry.Render(view, graphics);
        }
    }
}
