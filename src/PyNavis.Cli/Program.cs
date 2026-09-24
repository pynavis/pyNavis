using System;
using System.IO;
using Microsoft.Win32;

namespace PyNavis.Cli
{
    /// <summary>Thin arg-parser over CliCore with the real machine's paths.</summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // exe lives in bin\cli\ -> builds live in bin\<year>\
            var binRoot = Path.GetDirectoryName(Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location));
            var configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "pyNavis", "config.json");
            var bundleRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Autodesk", "ApplicationPlugins", "pyNavis.bundle");
            var cli = new CliCore(binRoot, configPath, bundleRoot, ResolveNavisDir, Console.Out);

            try
            {
                switch (args.Length > 0 ? args[0].ToLowerInvariant() : "")
                {
                    case "env":
                        return cli.Env();
                    case "attach" when args.Length == 2:
                        return cli.Attach(args[1]);
                    case "detach" when args.Length == 2:
                        return cli.Detach(args[1]);
                    case "extensions" when args.Length >= 2 && args[1] == "list":
                        return cli.ExtensionsList();
                    case "extensions" when args.Length >= 3 && args[1] == "add":
                        return cli.ExtensionsAdd(args[2]);
                    default:
                        Console.WriteLine("pynavis - pyNavis command line");
                        Console.WriteLine();
                        Console.WriteLine("  pynavis env                     show detected Navisworks versions and state");
                        Console.WriteLine($"  pynavis attach <year>           put the loader in the per-user bundle ({CliCore.SupportedVersionsNote})");
                        Console.WriteLine("  pynavis detach <year>           remove the loader from the bundle");
                        Console.WriteLine("  pynavis extensions list         show configured extension roots");
                        Console.WriteLine("  pynavis extensions add <dir>    add an extension root to config.json");
                        return 2;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
                return 1;
            }
        }

        private static string ResolveNavisDir(string year)
        {
            int y;
            if (!int.TryParse(year, out y)) return null;
            var regPath = $@"SOFTWARE\Autodesk\Navisworks Manage\{y - 2003}.0\Location";
            using (var key = Registry.LocalMachine.OpenSubKey(regPath))
            {
                var dir = key?.GetValue("Path") as string;
                if (dir != null && Directory.Exists(dir)) return dir.TrimEnd('\\');
            }
            foreach (var root in new[] { @"C:\Program Files", @"D:\Program Files" })
            {
                var candidate = Path.Combine(root, "Autodesk", "Navisworks Manage " + year);
                if (Directory.Exists(candidate)) return candidate;
            }
            return null;
        }
    }
}
