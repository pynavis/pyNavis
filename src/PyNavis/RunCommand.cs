using System;
using System.Runtime.CompilerServices;
using Autodesk.Navisworks.Api.Plugins;

namespace PyNavis
{
    /// <summary>
    /// The add-in <c>pynavis run</c> invokes through the Automation API, once per model,
    /// with the path of a request file. AddInLocation.None, like ConsoleCommand: it is
    /// reached by id only and puts no button on the "Tool add-ins" tab. Returns 0 when the
    /// script succeeded, 1 when it failed, 2 when the request could not be read.
    /// </summary>
    [Plugin("PyNavis.Run", "PYNV",
        DisplayName = "pyNavis Run",
        ToolTip = "Runs a script for pynavis run")]
    [AddInPlugin(AddInLocation.None)]
    public class RunCommand : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            // Run is the first thing the runtime is needed for when the CLI starts a fresh
            // Navisworks, so make sure PyNavis.Runtime.dll can be found, as the other
            // plugins do.
            AssemblyResolver.Install();

            if (parameters == null || parameters.Length != 1)
            {
                LoaderLog.Error($"pyNavis run expects one request file path, got {parameters?.Length ?? 0} parameter(s).");
                return 2;
            }

            try
            {
                return Run(parameters[0]);
            }
            catch (Exception ex)
            {
                // Logged, never a dialog: the Navisworks running this is hidden, so a message
                // box would wait for a click that never comes and hang the CLI with it.
                LoaderLog.Error("pyNavis run failed. " + ex);
                return 1;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int Run(string requestPath) =>
            Runtime.Execution.Runner.RunExecutor.RunRequestFile(requestPath);
    }
}
