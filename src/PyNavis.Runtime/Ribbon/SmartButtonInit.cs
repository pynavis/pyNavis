using System;
using Autodesk.Windows;
using PyNavis.Runtime.Bundles;
using PyNavis.Runtime.Engine;

namespace PyNavis.Runtime.Ribbon
{
    /// <summary>
    /// Runs a smartbutton's script.py at ribbon build with __selfinit__ True and a
    /// __button__ proxy, so the script can set its own title, tooltip, icon, and
    /// enabled state. Failures log and leave a plain button.
    /// </summary>
    public static class SmartButtonInit
    {
        public static void Run(PushButtonModel model, RibbonItem item)
        {
            try
            {
                var writer = new Output.LogWriter("selfinit " + model.BundleKey);
                var request = new ScriptRequest
                {
                    ScriptPath = model.ScriptPath,
                    Output = writer,
                    ErrorOutput = writer,
                };
                foreach (var path in model.SearchPaths) request.SearchPaths.Add(path);
                request.Globals["__file__"] = model.ScriptPath;
                request.Globals["__selfinit__"] = true;
                request.Globals["__button__"] = new SmartButtonProxy(item);
                request.Globals["__pynavis__"] = Execution.PyNavisHost.Instance;

                var result = EngineManager.GetEngine(model.EngineId).Execute(request);
                if (!result.Succeeded)
                    Log.Error($"Smartbutton init failed for {model.BundleKey}:\n{result.ErrorText}");
            }
            catch (Exception ex)
            {
                Log.Error($"Smartbutton init failed for {model.BundleKey}", ex);
            }
        }
    }

    /// <summary>The __button__ global handed to a smartbutton's selfinit run.</summary>
    public class SmartButtonProxy
    {
        private readonly RibbonItem _item;

        public SmartButtonProxy(RibbonItem item) { _item = item; }

        public string Title
        {
            get { return _item.Text; }
            set { _item.Text = value; }
        }

        public bool Enabled
        {
            get { return _item.IsEnabled; }
            set { _item.IsEnabled = value; }
        }

        public string Tooltip
        {
            set { _item.ToolTip = value; }
        }

        /// <summary>Points both icon slots at the given png (theme handling is the script's business).</summary>
        public void SetIcon(string path) => AdWindowsRibbonProvider.ApplyIcons(_item, path, path);
    }
}
