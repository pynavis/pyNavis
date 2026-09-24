using System;
using System.Runtime.CompilerServices;
using Autodesk.Navisworks.Api.Plugins;

namespace PyNavis
{
    /// <summary>
    /// pyNavis bootstrap. EventWatcherPlugin is the one plugin type Navisworks loads
    /// eagerly at application start - which is what lets pyNavis build its ribbon and
    /// run extension startup scripts without waiting for a user click.
    /// </summary>
    [Plugin("PyNavis.Boot", "PYNV",
        DisplayName = "pyNavis",
        ToolTip = "pyNavis bootstrap - loads the pyNavis runtime at startup")]
    public class BootPlugin : EventWatcherPlugin
    {
        // Installed before any other code in this class runs, so the JIT never sees a
        // PyNavis.Runtime type before the resolver can find it.
        static BootPlugin()
        {
            AssemblyResolver.Install();
        }

        public override void OnLoaded()
        {
            LoaderLog.Info($"BootPlugin.OnLoaded (loader for Navisworks {LoaderInfo.NavisVersion})");
            try
            {
                Bootstrap();
            }
            catch (Exception ex)
            {
                LoaderLog.ReportFatal("pyNavis failed to start.", ex);
            }
        }

        public override void OnUnloading()
        {
            LoaderLog.Info("BootPlugin.OnUnloading");
        }

        // NoInlining keeps every PyNavis.Runtime type reference out of OnLoaded's JIT
        // compilation, so a missing runtime surfaces as a catchable exception here
        // instead of a type-load failure before the try block exists.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Bootstrap()
        {
            Runtime.RuntimeHost.Boot();
        }
    }
}
