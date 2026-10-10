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
                request.Globals["__button__"] = new SmartButtonProxy(item, model);
                request.Globals["__pynavis__"] = Execution.PyNavisHost.Instance;
                request.Globals["__runner__"] = false;   // only pynavis run sets it True

                // Not a run telemetry records; a note in it goes nowhere.
                ExecResult result;
                using (Execution.PyNavisHost.Instance.TelemetryScope(null))
                    result = EngineManager.GetEngine(model.EngineId).Execute(request);
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
        private readonly PushButtonModel _model;

        /// <summary>With a model, a title or tooltip the script sets keeps the bundle's
        /// status: an experimental button stays marked whatever selfinit writes.</summary>
        public SmartButtonProxy(RibbonItem item, PushButtonModel model = null)
        {
            _item = item;
            _model = model;
        }

        public string Title
        {
            get { return _item.Text; }
            set { _item.Text = _model == null ? value : ButtonCaption.Caption(_model, value); }
        }

        public bool Enabled
        {
            get { return _item.IsEnabled; }
            set { _item.IsEnabled = value; }
        }

        public string Tooltip
        {
            set { _item.ToolTip = _model != null && _model.IsExperimental ? ButtonCaption.Tooltip(_model, value) : value; }
        }

        /// <summary>Points both icon slots at the given png (theme handling is the script's business).</summary>
        public void SetIcon(string path) => AdWindowsRibbonProvider.ApplyIcons(_item, path, path);
    }
}
