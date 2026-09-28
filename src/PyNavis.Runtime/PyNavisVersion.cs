using System;
using System.IO;
using System.Reflection;

namespace PyNavis.Runtime
{
    /// <summary>
    /// The one version number. Directory.Build.props reads it from
    /// pynavis/__init__.py at build and stamps it into every assembly, so the loader,
    /// the runtime, the command line, the installer and the Python library all agree.
    /// This is what the boot log, the Settings window and PyNavisHost.Version show,
    /// so someone debugging an install can read it in three places.
    /// </summary>
    public static class PyNavisVersion
    {
        /// <summary>"1.0.0": the product version, not the four-part assembly one.</summary>
        public static string Product { get; } = ReadProduct();

        public static string RuntimePath => typeof(PyNavisVersion).Assembly.Location;

        private static readonly Lazy<string> ApiVersion = new Lazy<string>(ReadNavisApiVersion);

        /// <summary>The Navisworks API assembly version, "24.0.x.x" and so on; "unknown"
        /// outside Navisworks.</summary>
        public static string HostApiVersion => ApiVersion.Value;

        /// <summary>Host release year (2023-2027), 0 when it cannot be told.</summary>
        public static int HostYear => Execution.HostGate.YearFromApiVersion(HostApiVersion) ?? 0;

        /// <summary>Resolved by name, not by a type reference: a type reference would make
        /// the JIT load the API assembly before any catch could run, and the test host
        /// does not have it. Inside Navisworks the assembly is already loaded.</summary>
        private static string ReadNavisApiVersion()
        {
            try
            {
                foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
                    if (loaded.GetName().Name == "Autodesk.Navisworks.Api")
                        return loaded.GetName().Version.ToString();
                return Assembly.Load(new AssemblyName("Autodesk.Navisworks.Api")).GetName().Version.ToString();
            }
            catch (Exception ex)
            {
                Log.Error("Could not read Navisworks API version: " + ex.Message);
                return "unknown";
            }
        }

        /// <summary>One line for a log or a bug report: version, host, runtime folder.</summary>
        public static string Summary()
        {
            var year = HostYear;
            var host = year > 0 ? "Navisworks " + year : "Navisworks (version unknown)";
            return $"pyNavis {Product} for {host}, runtime {RuntimePath}";
        }

        private static string ReadProduct()
        {
            var assembly = typeof(PyNavisVersion).Assembly;
            var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                var plus = info.IndexOf('+');            // strip a source-link build hash
                return plus > 0 ? info.Substring(0, plus) : info;
            }
            var v = assembly.GetName().Version;
            return v == null ? "unknown" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }
}
