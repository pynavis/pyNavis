using System;
using Autodesk.Windows;

namespace PyNavis.Runtime.Ribbon
{
    /// <summary>Ribbon-availability timing for early (EventWatcherPlugin) startup.</summary>
    public static class RibbonBootstrap
    {
        /// <summary>
        /// Runs <paramref name="build"/> as soon as the AdWindows ribbon exists.
        /// At EventWatcherPlugin load time the ribbon may not be constructed yet, so
        /// fall back to waiting on ComponentManager.ItemInitialized (the pattern the
        /// Revit add-in community uses for early ribbon access).
        /// </summary>
        public static void WhenRibbonReady(Action build)
        {
            if (ComponentManager.Ribbon != null)
            {
                Log.Info("Ribbon already available - building immediately.");
                build();
                return;
            }

            Log.Info("Ribbon not yet available - waiting for ComponentManager.ItemInitialized.");
            EventHandler<RibbonItemEventArgs> handler = null;
            handler = (sender, e) =>
            {
                if (ComponentManager.Ribbon == null) return;
                ComponentManager.ItemInitialized -= handler;
                Log.Info("Ribbon became available via ItemInitialized - building.");
                build();
            };
            ComponentManager.ItemInitialized += handler;
        }
    }
}
