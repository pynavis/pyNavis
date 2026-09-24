using System;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using PyNavis.Runtime.Bundles;
using PyNavis.Runtime.Engine;
using PyNavis.Runtime.Execution;

namespace PyNavis.Runtime.Panes
{
    /// <summary>Turns a *.dockpane bundle into the WPF element its shell displays.</summary>
    public static class PaneContentBuilder
    {
        /// <summary>Loads pane.xaml and runs the bundle's script.py once, if it has one.
        /// Never throws: a broken panel shows why in its own pane instead of taking
        /// Navisworks down during a dock layout restore.</summary>
        public static UIElement Build(DockPaneModel model, PaneShell shell)
        {
            UIElement content;
            try
            {
                content = LoadXaml(model.XamlPath);
            }
            catch (Exception ex)
            {
                Log.Error($"Dockpane '{model.Title}': pane.xaml failed to load", ex);
                shell.ShowMessage("Panel content failed to load", ex.Message);
                return shell.CurrentContent;
            }

            if (content == null)
            {
                // A <Window> root whose Content is null or not itself a UIElement. The
                // docs invite <Window> as a fallback root, so this is reachable, not just
                // theoretical, and must not leave the pane silently blank.
                Log.Error($"Dockpane '{model.Title}': pane.xaml's <Window> root has no " +
                          "UIElement Content - the pane would be blank.");
                shell.ShowMessage("Panel content is empty",
                    "pane.xaml's root produced no displayable content. See the pyNavis log.");
                return shell.CurrentContent;
            }

            if (model.ScriptPath != null)
            {
                try
                {
                    RunScript(model, content);
                }
                catch (Exception ex)
                {
                    Log.Error($"Dockpane '{model.Title}': script.py failed", ex);
                    Forms.Toast.Show("error", "Panel script failed: " + model.Title, ex.Message);
                }
            }
            return content;
        }

        private static UIElement LoadXaml(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                var root = XamlReader.Load(stream);
                // A bundle may ship a <Window> by habit; a Window cannot be a child, so
                // take its content instead of failing.
                var window = root as Window;
                if (window != null)
                {
                    var inner = window.Content as UIElement;
                    window.Content = null;
                    return inner;
                }
                return (UIElement)root;
            }
        }

        private static void RunScript(DockPaneModel model, UIElement content)
        {
            var request = new ScriptRequest { ScriptPath = model.ScriptPath };
            foreach (var path in model.SearchPaths)
                request.SearchPaths.Add(path);

            request.Globals["__file__"] = model.ScriptPath;
            request.Globals["__commandpath__"] = model.Directory;
            request.Globals["__title__"] = model.Title;
            request.Globals["__selfinit__"] = false;
            request.Globals["__pynavis__"] = PyNavisHost.Instance;
            request.Globals["__pane__"] = new PaneProxy(model, content);
            PyNavisHost.Instance.SetCommandContext(
                model.ScriptPath, model.Directory, model.Title, model.BundleKey);

            var result = EngineManager.GetEngine(model.EngineId).Execute(request);
            if (!result.Succeeded)
            {
                Log.Error($"Dockpane script '{model.Title}' failed:\n{result.ErrorText}");
                Forms.Toast.Show("error", "Panel script failed: " + model.Title,
                    "See the pyNavis log for the traceback.");
            }
        }
    }

    /// <summary>The <c>__pane__</c> global handed to a dockpane's script.py.</summary>
    public class PaneProxy
    {
        private readonly DockPaneModel _model;
        private readonly UIElement _content;

        internal PaneProxy(DockPaneModel model, UIElement content)
        {
            _model = model;
            _content = content;
        }

        public string BundleKey { get { return _model.BundleKey; } }
        public string Title { get { return _model.Title; } }
        public UIElement Content { get { return _content; } }

        /// <summary>The named element from pane.xaml, or null.</summary>
        public object Find(string name)
        {
            var element = _content as FrameworkElement;
            return element == null ? null : element.FindName(name);
        }

        public bool Visible
        {
            get { return PaneRegistry.IsVisible(_model.BundleKey); }
            set { PaneRegistry.SetVisible(_model.BundleKey, value); }
        }
    }
}
