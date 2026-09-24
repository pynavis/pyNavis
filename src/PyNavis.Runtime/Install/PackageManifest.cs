using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace PyNavis.Runtime.Install
{
    /// <summary>
    /// Edits the bundle's PackageContents.xml: one Components block per Navisworks
    /// release, each with a RuntimeRequirements line naming the series and one
    /// ComponentEntry per plugin DLL. The installer writes the loader entries; the
    /// runtime adds and removes the generated PyNavisPanes entry here.
    /// </summary>
    public static class PackageManifest
    {
        public const string ProductCode = "{7F3E9A2C-5B14-4D6E-9C7A-2E8B1F4D6A90}";
        public const string UpgradeCode = "{A1C4E7B2-3D58-4F9A-8B6C-5E2D7A9F1C43}";

        /// <summary>A manifest with no Components yet.</summary>
        public static XDocument CreateEmpty(string version)
        {
            return new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("ApplicationPackage",
                    new XAttribute("SchemaVersion", "1.0"),
                    new XAttribute("ProductType", "Application"),
                    new XAttribute("Name", "pyNavis"),
                    new XAttribute("Description", "Python scripting and ribbon tools for Navisworks"),
                    new XAttribute("AppVersion", version),
                    new XAttribute("FriendlyVersion", version),
                    new XAttribute("ProductCode", ProductCode),
                    new XAttribute("UpgradeCode", UpgradeCode),
                    new XAttribute("Author", "pyNavis"),
                    new XAttribute("SupportedLocales", "Enu"),
                    new XAttribute("OnlineDocumentation", "https://wiki.pynavis.com"),
                    new XElement("CompanyDetails",
                        new XAttribute("Name", "pyNavis"),
                        new XAttribute("Url", "https://pynavis.com"))));
        }

        /// <summary>The module path a manifest uses for a DLL in Contents\{year}.</summary>
        public static string ModulePath(string year, string fileName) => "./Contents/" + year + "/" + fileName;

        /// <summary>The Components block for a year, or null.</summary>
        public static XElement FindYear(XDocument doc, string year)
        {
            var marker = "/Contents/" + year + "/";
            return doc.Root?.Elements("Components").FirstOrDefault(c =>
                c.Elements("ComponentEntry").Any(e =>
                    ((string)e.Attribute("ModuleName") ?? "").Replace('\\', '/')
                        .IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        /// <summary>Adds a ComponentEntry for {year}, creating the year's Components
        /// block when it does not exist yet. Idempotent.</summary>
        public static void Register(XDocument doc, string year, string fileName, string appName, string version)
        {
            var block = FindYear(doc, year);
            if (block == null)
            {
                var series = BundleLayout.SeriesFor(year);
                block = new XElement("Components",
                    new XAttribute("Description", "Navisworks " + year),
                    new XElement("RuntimeRequirements",
                        new XAttribute("OS", "Win64"),
                        new XAttribute("Platform", "NAVMAN|NAVSIM"),
                        new XAttribute("SeriesMin", series),
                        new XAttribute("SeriesMax", series)));
                doc.Root.Add(block);
            }
            var module = ModulePath(year, fileName);
            if (block.Elements("ComponentEntry").Any(e => SameModule(e, module))) return;
            block.Add(new XElement("ComponentEntry",
                new XAttribute("AppName", appName),
                new XAttribute("AppType", "ManagedPlugin"),
                new XAttribute("Version", version),
                new XAttribute("ModuleName", module)));
        }

        /// <summary>Removes the ComponentEntry for {year}\{fileName}. A block left with
        /// no entries is removed too. Returns whether anything changed.</summary>
        public static bool Unregister(XDocument doc, string year, string fileName)
        {
            var block = FindYear(doc, year);
            if (block == null) return false;
            var module = ModulePath(year, fileName);
            var entries = block.Elements("ComponentEntry").Where(e => SameModule(e, module)).ToList();
            if (entries.Count == 0) return false;
            foreach (var e in entries) e.Remove();
            if (!block.Elements("ComponentEntry").Any()) block.Remove();
            return true;
        }

        /// <summary>Whether the manifest still lists any plugin at all.</summary>
        public static bool IsEmpty(XDocument doc) =>
            doc.Root == null || !doc.Root.Elements("Components").Any();

        public static XDocument Load(string path) => XDocument.Load(path);

        /// <summary>Writes through a temp file and swap: Navisworks reads this at every
        /// start, so an interrupted write must never leave it half done.</summary>
        public static void Save(XDocument doc, string path)
        {
            var temp = path + ".tmp";
            doc.Save(temp);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        private static bool SameModule(XElement entry, string module) =>
            string.Equals(((string)entry.Attribute("ModuleName") ?? "").Replace('\\', '/'),
                module, StringComparison.OrdinalIgnoreCase);
    }
}
