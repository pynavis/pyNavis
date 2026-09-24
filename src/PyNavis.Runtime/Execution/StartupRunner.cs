using System;
using System.Collections.Generic;
using PyNavis.Runtime.Bundles;
using PyNavis.Runtime.Engine;

namespace PyNavis.Runtime.Execution
{
    /// <summary>
    /// Runs each extension's startup.py (boot and every reload, before its ribbon
    /// renders). A failure logs and toasts but the extension's buttons still load.
    /// </summary>
    public static class StartupRunner
    {
        public static void RunAll(IReadOnlyList<ExtensionModel> extensions)
        {
            foreach (var ext in extensions)
            {
                if (ext.StartupScriptPath == null) continue;
                Log.Info($"Running startup.py for '{ext.Name}'.");
                var source = "startup " + ext.Name;
                // Same reason as HookRunner: startup.py sets no command context, so without
                // a log scope its lines would be labelled with the last command to run - at
                // boot that is nothing, but on a Reload it is Reload itself.
                PyNavisHost.Instance.LogSource = source;
                try
                {
                    var writer = new Output.LogWriter(source);
                    var request = new ScriptRequest
                    {
                        ScriptPath = ext.StartupScriptPath,
                        Output = writer,
                        ErrorOutput = writer,
                    };
                    request.SearchPaths.Add(ext.Directory);
                    if (ext.LibDirectory != null) request.SearchPaths.Add(ext.LibDirectory);
                    request.Globals["__file__"] = ext.StartupScriptPath;
                    request.Globals["__pynavis__"] = PyNavisHost.Instance;

                    var result = EngineManager.GetEngine(ext.DefaultEngineId).Execute(request);
                    if (!result.Succeeded)
                    {
                        Log.Error($"startup.py failed for '{ext.Name}':\n{result.ErrorText}");
                        Forms.Toast.Show("warning", $"{ext.Name} startup.py failed",
                            "See the log; the extension's buttons still loaded.");
                    }
                }
                catch (Exception ex)
                {
                    // e.g. unknown engine id, engine creation failure - as visible as a
                    // script that ran and failed, not a log-only silence.
                    Log.Error($"startup.py failed for '{ext.Name}'", ex);
                    Forms.Toast.Show("warning", $"{ext.Name} startup.py failed",
                        "See the log; the extension's buttons still loaded.");
                }
                finally
                {
                    PyNavisHost.Instance.LogSource = null;
                }
            }
        }
    }
}
