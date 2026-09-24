using System;
using System.IO;
using System.Windows.Media;
using PyNavis.Runtime.Config;

namespace PyNavis.Runtime.Output
{
    /// <summary>
    /// Answers "is the host UI dark?". Config "theme" key ("dark"/"light") wins;
    /// otherwise the AdWindows ribbon background is sampled, so this tracks whatever
    /// theme the running Navisworks actually renders - releases without a dark theme
    /// simply always read light. Never throws: unknown = light.
    /// </summary>
    public static class PyNavisTheme
    {
        private static bool? _cached;

        public static bool IsDark
        {
            get
            {
                if (_cached == null)
                {
                    var configPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "pyNavis", "config.json");
                    _cached = ResolveIsDark(PyNavisConfig.Load(configPath).Theme, SampleRibbonIsDark);
                }
                return _cached.Value;
            }
        }

        /// <summary>Drop the cached answer (Reload calls this so a theme change is picked up).</summary>
        public static void Invalidate() => _cached = null;

        /// <summary>Pure precedence: explicit override beats detection; anything else = detect.</summary>
        public static bool ResolveIsDark(string configTheme, Func<bool> detector)
        {
            if (string.Equals(configTheme, "dark", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(configTheme, "light", StringComparison.OrdinalIgnoreCase)) return false;
            try
            {
                return detector();
            }
            catch (Exception ex)
            {
                Log.Error("Theme detection failed - assuming light.", ex);
                return false;
            }
        }

        private static bool SampleRibbonIsDark()
        {
            var ribbon = Autodesk.Windows.ComponentManager.Ribbon;
            var brush = ribbon?.Background as SolidColorBrush;
            if (brush == null) return false;
            var c = brush.Color;
            return ThemeMath.IsDarkColor(c.R, c.G, c.B);
        }
    }
}
