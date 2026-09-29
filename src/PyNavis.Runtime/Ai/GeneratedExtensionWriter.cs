using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PyNavis.Runtime.Bundles;

namespace PyNavis.Runtime.Ai
{
    /// <summary>A generated tool on disk.</summary>
    public sealed class GeneratedTool
    {
        public string Directory;
        public string Title;
    }

    /// <summary>
    /// The one extension the assistant may write into: AI.extension under the user's
    /// extension root, one pushbutton per tool on a single "Generated" panel. Bundles
    /// are written to a temp folder and moved into place, so a half-written tool never
    /// meets a Reload. The writer refuses to touch any folder outside its extension.
    /// </summary>
    public sealed class GeneratedExtensionWriter
    {
        public const string ExtensionFolder = "AI.extension";
        public const string TabFolder = "AI.tab";
        public const string PanelFolder = "Generated.panel";
        private const int MaxFolderName = 40;

        private readonly IIconWriter _icons;

        public string Root { get; }
        public string PanelDir => Path.Combine(Root, TabFolder, PanelFolder);

        public GeneratedExtensionWriter(string root, IIconWriter icons)
        {
            Root = Path.GetFullPath(root ?? throw new ArgumentNullException(nameof(root))).TrimEnd('\\', '/');
            _icons = icons ?? throw new ArgumentNullException(nameof(icons));
        }

        /// <summary>The per-user extension root pyNavis always scans, so a Reload finds
        /// the extension with no config change.</summary>
        public static string DefaultRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "pyNavis", "extensions", ExtensionFolder);

        public static GeneratedExtensionWriter Default => new GeneratedExtensionWriter(DefaultRoot, new IconGenerator());

        public bool Owns(string bundleDir)
        {
            if (string.IsNullOrWhiteSpace(bundleDir)) return false;
            var full = Path.GetFullPath(bundleDir).TrimEnd('\\', '/');
            return full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Creates the extension skeleton if it is not there yet.</summary>
        public void EnsureExtension()
        {
            Directory.CreateDirectory(PanelDir);
            var yaml = Path.Combine(Root, "extension.yaml");
            if (!File.Exists(yaml))
                File.WriteAllText(yaml, "name: AI\nengine: ironpython\n", new UTF8Encoding(false));
        }

        /// <summary>Writes a new bundle and returns its folder.</summary>
        public string Create(Proposal proposal)
        {
            Validate(proposal);
            EnsureExtension();
            var target = UniqueBundleDir(SafeFolderName(proposal.Title));
            var temp = Path.Combine(PanelDir, ".tmp-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temp);
                WriteFiles(temp, proposal);
                _icons.Write(temp, proposal.Icon);
                Directory.Move(temp, target);
            }
            finally
            {
                try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { }
            }
            return target;
        }

        /// <summary>Rewrites an existing generated bundle's files in place. The folder
        /// name (and so the bundle key) stays, so shortcuts keep pointing at it.</summary>
        public void Update(string bundleDir, Proposal proposal)
        {
            Validate(proposal);
            if (!Owns(bundleDir))
                throw new InvalidOperationException("Only tools inside " + ExtensionFolder + " can be rewritten.");
            if (!Directory.Exists(bundleDir))
                throw new DirectoryNotFoundException("The tool's folder is gone: " + bundleDir);

            WriteFiles(bundleDir, proposal);
            foreach (var name in Proposal.AllowedFiles)
                if (!proposal.Files.ContainsKey(name) && name != "bundle.yaml")
                    TryDelete(Path.Combine(bundleDir, name));
            _icons.Write(bundleDir, proposal.Icon);
        }

        /// <summary>Every generated tool, by title.</summary>
        public List<GeneratedTool> Tools()
        {
            var tools = new List<GeneratedTool>();
            if (!Directory.Exists(PanelDir)) return tools;
            foreach (var dir in Directory.GetDirectories(PanelDir, "*.pushbutton")
                                        .OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var title = TitleOf(dir);
                tools.Add(new GeneratedTool { Directory = dir, Title = title });
            }
            return tools;
        }

        /// <summary>The files of a generated bundle, for putting back into a conversation.</summary>
        public Dictionary<string, string> ReadFiles(string bundleDir)
        {
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in Proposal.AllowedFiles)
            {
                var path = Path.Combine(bundleDir, name);
                if (File.Exists(path)) files[name] = File.ReadAllText(path);
            }
            return files;
        }

        private static string TitleOf(string dir)
        {
            var yamlPath = Path.Combine(dir, "bundle.yaml");
            try
            {
                if (File.Exists(yamlPath))
                {
                    var yaml = BundleYaml.Parse(File.ReadAllText(yamlPath));
                    if (yaml.TryGetValue("title", out var t) && !string.IsNullOrWhiteSpace(t)) return t.Trim();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Could not read '{yamlPath}'", ex);
            }
            var folder = Path.GetFileName(dir);
            folder = folder.Substring(0, folder.Length - ".pushbutton".Length);
            return Bundles.BundleKeys.StripPrefix(folder).Replace('_', ' ');
        }

        // ---- pieces --------------------------------------------------------------

        private static void Validate(Proposal proposal)
        {
            if (proposal == null) throw new ArgumentNullException(nameof(proposal));
            if (!proposal.Files.ContainsKey("script.py"))
                throw new InvalidOperationException("The proposal has no script.py, so there is nothing to run.");
            if (string.IsNullOrWhiteSpace(proposal.Title))
                throw new InvalidOperationException("The proposal has no title.");
        }

        private static void WriteFiles(string dir, Proposal proposal)
        {
            var utf8 = new UTF8Encoding(false);
            foreach (var pair in proposal.Files)
            {
                if (!Proposal.AllowedFiles.Contains(pair.Key, StringComparer.OrdinalIgnoreCase)) continue;
                File.WriteAllText(Path.Combine(dir, pair.Key.ToLowerInvariant()), Normalise(pair.Value), utf8);
            }
            if (!proposal.Files.ContainsKey("bundle.yaml"))
                File.WriteAllText(Path.Combine(dir, "bundle.yaml"),
                    "title: " + proposal.Title.Trim() + "\n", utf8);
        }

        private static string Normalise(string text)
        {
            var t = (text ?? "").Replace("\r\n", "\n");
            return t.EndsWith("\n") ? t : t + "\n";
        }

        private string UniqueBundleDir(string baseName)
        {
            var candidate = Path.Combine(PanelDir, baseName + ".pushbutton");
            for (var n = 2; Directory.Exists(candidate); n++)
                candidate = Path.Combine(PanelDir, baseName + "_" + n + ".pushbutton");
            return candidate;
        }

        /// <summary>A folder name from a title: letters, digits and underscores only,
        /// never empty, never starting with the digits-underscore run the parser reads
        /// as an ordering prefix, at most 40 characters.</summary>
        public static string SafeFolderName(string title)
        {
            var cleaned = Regex.Replace(title ?? "", @"[^A-Za-z0-9 _\-]+", "");
            cleaned = Regex.Replace(cleaned.Trim(), @"[\s\-]+", "_");
            cleaned = Regex.Replace(cleaned, @"_{2,}", "_").Trim('_');
            if (cleaned.Length == 0) return "Tool";
            if (Regex.IsMatch(cleaned, @"^\d")) cleaned = "Tool_" + cleaned;
            if (cleaned.Length > MaxFolderName) cleaned = cleaned.Substring(0, MaxFolderName).TrimEnd('_');
            return cleaned;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
