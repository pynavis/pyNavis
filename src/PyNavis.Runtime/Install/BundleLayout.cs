using System;
using System.IO;

namespace PyNavis.Runtime.Install
{
    /// <summary>
    /// The per-user Autodesk bundle pyNavis installs into:
    ///   %APPDATA%\Autodesk\ApplicationPlugins\pyNavis.bundle\PackageContents.xml
    ///   %APPDATA%\Autodesk\ApplicationPlugins\pyNavis.bundle\Contents\{year}\PyNavis.dll
    /// Navisworks reads the manifest at startup and loads the ComponentEntry whose
    /// series matches the running release, so no administrator rights are involved.
    /// </summary>
    public sealed class BundleLayout
    {
        public const string BundleFolderName = "pyNavis.bundle";
        public const string ManifestFileName = "PackageContents.xml";

        /// <summary>The bundle root folder (ends in pyNavis.bundle).</summary>
        public string Root { get; }
        /// <summary>The Navisworks release year this loader belongs to, e.g. "2026".</summary>
        public string Year { get; }
        /// <summary>Contents\{year}: the folder holding the loader and any generated plugin.</summary>
        public string ContentsDir => Path.Combine(Root, "Contents", Year);
        public string ManifestPath => Path.Combine(Root, ManifestFileName);

        private BundleLayout(string root, string year)
        {
            Root = root;
            Year = year;
        }

        /// <summary>The bundle folder for the current user.</summary>
        public static string DefaultRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Autodesk", "ApplicationPlugins", BundleFolderName);

        /// <summary>Navisworks' internal series for a release year: 2026 is Nw23.</summary>
        public static string SeriesFor(string year)
        {
            int y;
            if (!int.TryParse(year, out y)) throw new ArgumentException("Not a release year: " + year);
            return "Nw" + (y - 2003);
        }

        /// <summary>
        /// Recognises a loader path of the shape ...\{anything}.bundle\Contents\{year}\PyNavis.dll
        /// and returns its layout, or null for a loader that lives anywhere else (the
        /// developer's Plugins folder, a test folder). Pure: nothing is touched on disk.
        /// </summary>
        public static BundleLayout FromLoaderPath(string loaderPath)
        {
            if (string.IsNullOrEmpty(loaderPath)) return null;
            var yearDir = Path.GetDirectoryName(loaderPath);
            if (yearDir == null) return null;
            var contentsDir = Path.GetDirectoryName(yearDir);
            if (contentsDir == null) return null;
            var root = Path.GetDirectoryName(contentsDir);
            if (root == null) return null;

            var year = Path.GetFileName(yearDir);
            int y;
            if (!int.TryParse(year, out y) || year.Length != 4) return null;
            if (!string.Equals(Path.GetFileName(contentsDir), "Contents", StringComparison.OrdinalIgnoreCase)) return null;
            if (!Path.GetFileName(root).EndsWith(".bundle", StringComparison.OrdinalIgnoreCase)) return null;
            return new BundleLayout(root, year);
        }
    }
}
