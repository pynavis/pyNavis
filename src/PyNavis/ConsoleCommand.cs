using System;
using System.Runtime.CompilerServices;
using Autodesk.Navisworks.Api.Plugins;

namespace PyNavis
{
    /// <summary>
    /// Programmatic entry point to the pyNavis console diagnostic. AddInLocation.None
    /// keeps the plugin registered and invocable (by id, by *.linkbutton, by
    /// ExecuteAddInPlugin) without adding a button to the auto-generated "Tool add-ins"
    /// ribbon tab, which pyNavis does not use.
    /// </summary>
    [Plugin("PyNavis.Console", "PYNV",
        DisplayName = "pyNavis Console",
        ToolTip = "Open the pyNavis console")]
    [AddInPlugin(AddInLocation.None)]
    public class ConsoleCommand : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            try
            {
                Run();
                return 0;
            }
            catch (Exception ex)
            {
                LoaderLog.ReportFatal("pyNavis console failed to start.", ex);
                return 1;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Run()
        {
            Runtime.RuntimeHost.RunConsoleFallback();
        }
    }
}
