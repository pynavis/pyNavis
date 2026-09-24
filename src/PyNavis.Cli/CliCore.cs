using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Xml.Linq;

namespace PyNavis.Cli
{
    /// <summary>
    /// The pynavis CLI's operations, decoupled from Main for testability: paths and
    /// Navisworks-install resolution are injected, output goes to a TextWriter.
    /// </summary>
    public class CliCore
    {
        public const string SupportedVersionsNote = "2023-2027";

        private readonly string _binRoot;
        private readonly string _configPath;
        private readonly string _bundleRoot;
        private readonly Func<string, string> _resolveNavisDir;
        private readonly TextWriter _out;

        /// <param name="bundleRoot">The per-user bundle folder, normally
        /// %APPDATA%\Autodesk\ApplicationPlugins\pyNavis.bundle.</param>
        public CliCore(string binRoot, string configPath, string bundleRoot, Func<string, string> resolveNavisDir, TextWriter output)
        {
            _binRoot = binRoot;
            _configPath = configPath;
            _bundleRoot = bundleRoot;
            _resolveNavisDir = resolveNavisDir;
            _out = output;
        }

        private string ContentsDir(string year) => Path.Combine(_bundleRoot, "Contents", year);
        private string ManifestPath => Path.Combine(_bundleRoot, "PackageContents.xml");
        private string LegacyPluginDir(string navisDir) => Path.Combine(navisDir, "Plugins", "PyNavis");

        public int Attach(string year)
        {
            var navisDir = _resolveNavisDir(year);
            if (navisDir == null || !File.Exists(Path.Combine(navisDir, "Roamer.exe")))
            {
                _out.WriteLine($"Navisworks Manage {year} not found (or is a reference drop without Roamer.exe).");
                return 1;
            }

            var loader = Path.Combine(_binRoot, year, "PyNavis.dll");
            if (!File.Exists(loader))
            {
                _out.WriteLine($"No pyNavis build for {year}: {loader} missing (run: dotnet build -p:NavisVersion={year}).");
                return 1;
            }

            var dir = ContentsDir(year);
            Directory.CreateDirectory(dir);
            File.Copy(loader, Path.Combine(dir, "PyNavis.dll"), true);
            var pdb = Path.ChangeExtension(loader, ".pdb");
            if (File.Exists(pdb)) File.Copy(pdb, Path.Combine(dir, "PyNavis.pdb"), true);

            var doc = File.Exists(ManifestPath) ? XDocument.Load(ManifestPath) : NewManifest();
            RegisterLoader(doc, year);
            SaveManifest(doc);

            _out.WriteLine($"Attached: {Path.Combine(dir, "PyNavis.dll")} (registered in {ManifestPath})");
            var legacy = LegacyPluginDir(navisDir);
            if (Directory.Exists(legacy))
                _out.WriteLine($"WARNING: legacy copy still present at {legacy}. Both would load; delete that folder (needs administrator rights).");
            return 0;
        }

        public int Detach(string year)
        {
            var dir = ContentsDir(year);
            var attached = Directory.Exists(dir);
            if (attached) Directory.Delete(dir, true);

            if (File.Exists(ManifestPath))
            {
                var doc = XDocument.Load(ManifestPath);
                var removed = UnregisterYear(doc, year);
                if (!doc.Root.Elements("Components").Any())
                {
                    File.Delete(ManifestPath);
                    var contents = Path.Combine(_bundleRoot, "Contents");
                    if (Directory.Exists(contents) && !Directory.EnumerateFileSystemEntries(contents).Any())
                        Directory.Delete(contents);
                    if (Directory.Exists(_bundleRoot) && !Directory.EnumerateFileSystemEntries(_bundleRoot).Any())
                        Directory.Delete(_bundleRoot);
                }
                else if (removed) SaveManifest(doc);
                attached |= removed;
            }

            if (!attached)
            {
                _out.WriteLine($"pyNavis is not attached to Navisworks {year}.");
                return 0;
            }
            _out.WriteLine($"Detached: Navisworks {year}.");
            return 0;
        }

        private static XDocument NewManifest()
        {
            return new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("ApplicationPackage",
                    new XAttribute("SchemaVersion", "1.0"),
                    new XAttribute("ProductType", "Application"),
                    new XAttribute("Name", "pyNavis"),
                    new XAttribute("Description", "Python scripting and ribbon tools for Navisworks"),
                    new XAttribute("AppVersion", "0.0.0"),
                    new XAttribute("FriendlyVersion", "dev"),
                    new XAttribute("ProductCode", "{7F3E9A2C-5B14-4D6E-9C7A-2E8B1F4D6A90}"),
                    new XAttribute("UpgradeCode", "{A1C4E7B2-3D58-4F9A-8B6C-5E2D7A9F1C43}"),
                    new XAttribute("Author", "pyNavis"),
                    new XAttribute("SupportedLocales", "Enu"),
                    new XElement("CompanyDetails", new XAttribute("Name", "pyNavis"))));
        }

        private static XElement FindYear(XDocument doc, string year)
        {
            var marker = "/Contents/" + year + "/";
            return doc.Root.Elements("Components").FirstOrDefault(c =>
                c.Elements("ComponentEntry").Any(e =>
                    ((string)e.Attribute("ModuleName") ?? "").Replace('\\', '/')
                        .IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private static void RegisterLoader(XDocument doc, string year)
        {
            var module = "./Contents/" + year + "/PyNavis.dll";
            var block = FindYear(doc, year);
            if (block == null)
            {
                var series = "Nw" + (int.Parse(year) - 2003);
                block = new XElement("Components",
                    new XAttribute("Description", "Navisworks " + year),
                    new XElement("RuntimeRequirements",
                        new XAttribute("OS", "Win64"),
                        new XAttribute("Platform", "NAVMAN|NAVSIM"),
                        new XAttribute("SeriesMin", series),
                        new XAttribute("SeriesMax", series)));
                doc.Root.Add(block);
            }
            if (block.Elements("ComponentEntry").Any(e =>
                    string.Equals((string)e.Attribute("ModuleName"), module, StringComparison.OrdinalIgnoreCase)))
                return;
            block.AddFirst(new XElement("ComponentEntry",
                new XAttribute("AppName", "pyNavis"),
                new XAttribute("AppType", "ManagedPlugin"),
                new XAttribute("Version", (string)doc.Root.Attribute("AppVersion") ?? "0.0.0"),
                new XAttribute("ModuleName", module)));
        }

        private static bool UnregisterYear(XDocument doc, string year)
        {
            var block = FindYear(doc, year);
            if (block == null) return false;
            block.Remove();
            return true;
        }

        private void SaveManifest(XDocument doc)
        {
            Directory.CreateDirectory(_bundleRoot);
            var temp = ManifestPath + ".tmp";
            doc.Save(temp);
            if (File.Exists(ManifestPath)) File.Replace(temp, ManifestPath, null);
            else File.Move(temp, ManifestPath);
        }

        public int Env()
        {
            _out.WriteLine("pyNavis environment");
            _out.WriteLine($"  config : {_configPath}{(File.Exists(_configPath) ? "" : "  (absent)")}");
            _out.WriteLine($"  builds : {_binRoot}");
            _out.WriteLine($"  bundle : {_bundleRoot}{(File.Exists(ManifestPath) ? "" : "  (absent)")}");
            foreach (var year in new[] { "2023", "2024", "2025", "2026", "2027" })
            {
                var navisDir = _resolveNavisDir(year);
                string state;
                if (navisDir == null)
                    state = "not installed";
                else if (!File.Exists(Path.Combine(navisDir, "Roamer.exe")))
                    state = "reference drop (no Roamer.exe)";
                else if (File.Exists(Path.Combine(ContentsDir(year), "PyNavis.dll")))
                    state = Directory.Exists(LegacyPluginDir(navisDir))
                        ? "installed, pyNavis ATTACHED (and a legacy Plugins\\PyNavis copy: delete it)"
                        : "installed, pyNavis ATTACHED";
                else if (Directory.Exists(LegacyPluginDir(navisDir)))
                    state = "installed, legacy Plugins\\PyNavis copy only (re-attach, then delete it)";
                else
                    state = "installed, not attached";

                var built = File.Exists(Path.Combine(_binRoot, year, "PyNavis.dll")) ? "built" : "no build";
                _out.WriteLine($"  NW{year} : {state} ({built})");
            }
            foreach (var path in ReadConfig().Extensions)
                _out.WriteLine($"  extension root : {path}");
            return 0;
        }

        public int ExtensionsList()
        {
            var extensions = ReadConfig().Extensions;
            if (extensions.Count == 0)
                _out.WriteLine("No extension roots configured.");
            foreach (var path in extensions)
                _out.WriteLine(path);
            return 0;
        }

        public int ExtensionsAdd(string path)
        {
            var full = Path.GetFullPath(path);
            if (!Directory.Exists(full))
            {
                _out.WriteLine($"Directory does not exist: {full}");
                return 1;
            }

            var config = ReadConfig();
            if (config.Extensions.Contains(full, StringComparer.OrdinalIgnoreCase))
            {
                _out.WriteLine($"Already configured: {full}");
                return 0;
            }

            config.Extensions.Add(full);
            config.Data["extensions"] = config.Extensions.ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(_configPath));
            File.WriteAllText(_configPath, new JavaScriptSerializer().Serialize(config.Data));
            _out.WriteLine($"Added extension root: {full}");
            return 0;
        }

        private sealed class ConfigState
        {
            public Dictionary<string, object> Data = new Dictionary<string, object>();
            public List<string> Extensions = new List<string>();
        }

        private ConfigState ReadConfig()
        {
            var state = new ConfigState();
            try
            {
                if (File.Exists(_configPath))
                {
                    state.Data = new JavaScriptSerializer()
                        .Deserialize<Dictionary<string, object>>(File.ReadAllText(_configPath))
                        ?? new Dictionary<string, object>();
                }
            }
            catch (Exception ex)
            {
                _out.WriteLine($"Warning: could not parse {_configPath} ({ex.Message}) - treating as empty.");
            }

            if (state.Data.TryGetValue("extensions", out var raw) && raw is IEnumerable list && !(raw is string))
                foreach (var item in list)
                    if (item is string s && !string.IsNullOrWhiteSpace(s))
                        state.Extensions.Add(s);
            return state;
        }
    }
}
