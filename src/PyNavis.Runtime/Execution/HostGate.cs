using PyNavis.Runtime.Bundles;

namespace PyNavis.Runtime.Execution
{
    /// <summary>Bundle min/max host version check. Blocked buttons still render;
    /// the click warns and aborts (per design decision).</summary>
    public static class HostGate
    {
        /// <summary>Null when allowed to run; otherwise the toast message.</summary>
        public static string BlockMessage(PushButtonModel model, int hostYear)
        {
            // 0 is RuntimeHost's "could not parse the API version". A host we cannot
            // identify runs the tool: refusing would block every gated bundle at once.
            if (hostYear <= 0) return null;

            if (model.MinHostYear.HasValue && hostYear < model.MinHostYear.Value)
                return $"Requires Navisworks {model.MinHostYear.Value} or newer (this is {hostYear}).";
            if (model.MaxHostYear.HasValue && hostYear > model.MaxHostYear.Value)
                return $"Requires Navisworks {model.MaxHostYear.Value} or older (this is {hostYear}).";
            return null;
        }

        /// <summary>Navisworks internal major version is year - 2003 (20.x = 2023 ... 23.x = 2026).</summary>
        public static int? YearFromApiVersion(string apiVersion)
        {
            if (string.IsNullOrEmpty(apiVersion)) return null;
            var dot = apiVersion.IndexOf('.');
            var majorText = dot > 0 ? apiVersion.Substring(0, dot) : apiVersion;
            return int.TryParse(majorText, out var major) ? major + 2003 : (int?)null;
        }
    }
}
